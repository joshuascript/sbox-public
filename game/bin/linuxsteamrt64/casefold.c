/*
 * casefold.c — LD_PRELOAD case-insensitive path resolver for s&box on Linux.
 *
 * The Source 2 native engine assumes lowercase paths. s&box content uses mixed
 * case ("addons/menu/Code"). On case-sensitive filesystems (ext4, btrfs, xfs)
 * those opens fail. This shim intercepts libc file I/O, walks the requested
 * absolute path one segment at a time, and substitutes a case-insensitive
 * sibling whenever a segment doesn't exist on disk. Resolved paths are cached
 * keyed by the original (broken) input. Mutating ops (unlink/rmdir/rename)
 * drop matching cache entries.
 *
 * cwd shortcut: cwd is captured once at init and never changes after that.
 * Read paths access it without locking — only capture_cwd (called once from
 * init and optionally from chdir) writes under the mutex.
 *
 * Directory entry cache: each parent directory's entries are scanned once and
 * stored in a hash table keyed by the directory path. Subsequent segment-walk
 * lookups for the same parent are O(1) instead of opendir/readdir/closedir.
 * Entries are invalidated when mutations (mkdir/rmdir/rename/unlink) succeed.
 *
 * See training.md.
 */

#define _GNU_SOURCE
#include <dlfcn.h>
#include <stdio.h>
#include <stdarg.h>
#include <fcntl.h>
#include <sys/stat.h>
#include <sys/types.h>
#include <unistd.h>
#include <dirent.h>
#include <string.h>
#include <strings.h>
#include <stdlib.h>
#include <pthread.h>
#include <errno.h>
#include <limits.h>
#include <linux/stat.h>   /* struct statx, STATX_BASIC_STATS */

/* ===== Real libc pointers ============================================== */

typedef int    (*open_t)     (const char *, int, ...);
typedef int    (*open64_t)   (const char *, int, ...);
typedef int    (*openat_t)   (int, const char *, int, ...);
typedef int    (*openat64_t) (int, const char *, int, ...);
typedef FILE * (*fopen_t)    (const char *, const char *);
typedef FILE * (*fopen64_t)  (const char *, const char *);
typedef FILE * (*freopen_t)  (const char *, const char *, FILE *);
typedef int    (*stat_t)     (const char *, struct stat *);
typedef int    (*stat64_t)   (const char *, struct stat64 *);
typedef int    (*lstat_t)    (const char *, struct stat *);
typedef int    (*lstat64_t)  (const char *, struct stat64 *);
typedef int    (*fstatat_t)  (int, const char *, struct stat *, int);
typedef int    (*fstatat64_t)(int, const char *, struct stat64 *, int);
typedef int    (*statx_t)    (int, const char *, int, unsigned int, struct statx *);
typedef int    (*access_t)   (const char *, int);
typedef int    (*faccessat_t)(int, const char *, int, int);
typedef DIR *  (*opendir_t)  (const char *);
typedef int    (*unlink_t)   (const char *);
typedef int    (*unlinkat_t) (int, const char *, int);
typedef int    (*rmdir_t)    (const char *);
typedef int    (*rename_t)   (const char *, const char *);
typedef int    (*renameat_t) (int, const char *, int, const char *);
typedef int    (*mkdir_t)    (const char *, mode_t);
typedef int    (*mkdirat_t)  (int, const char *, mode_t);
typedef char * (*realpath_t) (const char *, char *);
typedef int    (*chmod_t)    (const char *, mode_t);
typedef int    (*chdir_t)    (const char *);

static open_t        real_open;
static open64_t      real_open64;
static openat_t      real_openat;
static openat64_t    real_openat64;
static fopen_t       real_fopen;
static fopen64_t     real_fopen64;
static freopen_t     real_freopen;
static stat_t        real_stat;
static stat64_t      real_stat64;
static lstat_t       real_lstat;
static lstat64_t     real_lstat64;
static fstatat_t     real_fstatat;
static fstatat64_t   real_fstatat64;
static statx_t       real_statx;
static access_t      real_access;
static faccessat_t   real_faccessat;
static opendir_t     real_opendir;
static unlink_t      real_unlink;
static unlinkat_t    real_unlinkat;
static rmdir_t       real_rmdir;
static rename_t      real_rename;
static renameat_t    real_renameat;
static mkdir_t       real_mkdir;
static mkdirat_t     real_mkdirat;
static realpath_t    real_realpath;
static chmod_t       real_chmod;
static chdir_t       real_chdir;

#define LOAD(sym) real_##sym = (sym##_t)dlsym(RTLD_NEXT, #sym)

static int debug_log = 0;
#define LOG(...) do { if (debug_log) fprintf(stderr, "[casefold] " __VA_ARGS__); } while (0)

/* ===== Captured cwd ==================================================== */
/*
 * Written once at init (and rarely by chdir). Read paths access cwd_buf and
 * cwd_len directly without locking — the pthread_once barrier in ENSURE_INIT
 * guarantees the write is visible before any hook runs. The mutex only
 * protects the rare chdir write path.
 */

static char            cwd_buf[PATH_MAX];
static size_t          cwd_len;
static pthread_mutex_t cwd_mutex = PTHREAD_MUTEX_INITIALIZER;

static void capture_cwd(void)
{
	char tmp[PATH_MAX];
	if (getcwd(tmp, sizeof(tmp)) == NULL) return;
	size_t n = strlen(tmp);
	pthread_mutex_lock(&cwd_mutex);
	memcpy(cwd_buf, tmp, n + 1);
	cwd_len = n;
	pthread_mutex_unlock(&cwd_mutex);
	LOG("cwd captured: %s\n", tmp);
}

/* Returns 1 if path is under the game root. Lock-free — cwd is stable after
 * init and pthread_once ensures visibility. */
static int path_under_cwd(const char *path)
{
	return (cwd_len > 0 &&
	        strncmp(path, cwd_buf, cwd_len) == 0 &&
	        (path[cwd_len] == '/' || path[cwd_len] == '\0'));
}

/* Copy the cwd prefix from path into out. Lock-free for same reason. */
static size_t copy_cwd_prefix(const char *path, char *out, size_t out_cap)
{
	size_t len = cwd_len;
	if (len == 0 || len + 1 > out_cap) return 0;
	if (strncmp(path, cwd_buf, len) != 0) return 0;
	if (path[len] != '/' && path[len] != '\0') return 0;
	memcpy(out, cwd_buf, len + 1);
	return len;
}

/* ===== Init ============================================================ */

static pthread_once_t init_once = PTHREAD_ONCE_INIT;
static void init_real(void)
{
	LOAD(open);    LOAD(open64);
	LOAD(openat);  LOAD(openat64);
	LOAD(fopen);   LOAD(fopen64); LOAD(freopen);
	LOAD(stat);    LOAD(stat64);
	LOAD(lstat);   LOAD(lstat64);
	LOAD(fstatat); LOAD(fstatat64);
	LOAD(statx);
	LOAD(access);  LOAD(faccessat);
	LOAD(opendir);
	LOAD(unlink);  LOAD(unlinkat); LOAD(rmdir);
	LOAD(rename);  LOAD(renameat);
	LOAD(mkdir);   LOAD(mkdirat);
	LOAD(realpath);
	LOAD(chmod);
	LOAD(chdir);

	if (getenv("CASEFOLD_DEBUG")) debug_log = 1;
	capture_cwd();
	LOG("initialized\n");
}
#define ENSURE_INIT() pthread_once(&init_once, init_real)

/* ===== Path cache (broken absolute path -> resolved absolute path) ===== */

#define CACHE_BUCKETS 4096

typedef struct cache_entry {
	char *key;
	char *val;
	struct cache_entry *next;
} cache_entry;

static cache_entry *cache_buckets[CACHE_BUCKETS];
static pthread_rwlock_t cache_lock = PTHREAD_RWLOCK_INITIALIZER;

static unsigned long path_hash(const char *s)
{
	unsigned long h = 5381;
	int c;
	while ((c = *s++)) h = ((h << 5) + h) + (unsigned char)c;
	return h;
}

static char *cache_get(const char *key)
{
	unsigned long b = path_hash(key) % CACHE_BUCKETS;
	char *out = NULL;
	pthread_rwlock_rdlock(&cache_lock);
	for (cache_entry *e = cache_buckets[b]; e; e = e->next) {
		if (strcmp(e->key, key) == 0) { out = strdup(e->val); break; }
	}
	pthread_rwlock_unlock(&cache_lock);
	return out;
}

static void cache_set(const char *key, const char *val)
{
	unsigned long b = path_hash(key) % CACHE_BUCKETS;
	pthread_rwlock_wrlock(&cache_lock);
	for (cache_entry *e = cache_buckets[b]; e; e = e->next) {
		if (strcmp(e->key, key) == 0) {
			free(e->val); e->val = strdup(val);
			pthread_rwlock_unlock(&cache_lock);
			return;
		}
	}
	cache_entry *ne = (cache_entry *)malloc(sizeof(*ne));
	if (!ne) { pthread_rwlock_unlock(&cache_lock); return; }
	ne->key = strdup(key); ne->val = strdup(val);
	ne->next = cache_buckets[b];
	cache_buckets[b] = ne;
	pthread_rwlock_unlock(&cache_lock);
}

static void cache_drop_key(const char *key)
{
	unsigned long b = path_hash(key) % CACHE_BUCKETS;
	pthread_rwlock_wrlock(&cache_lock);
	cache_entry **pp = &cache_buckets[b];
	while (*pp) {
		if (strcmp((*pp)->key, key) == 0) {
			cache_entry *t = *pp; *pp = t->next;
			free(t->key); free(t->val); free(t);
			break;
		}
		pp = &(*pp)->next;
	}
	pthread_rwlock_unlock(&cache_lock);
}

static void cache_drop_subtree(const char *path)
{
	if (!path || !*path) return;
	size_t plen = strlen(path);
	pthread_rwlock_wrlock(&cache_lock);
	for (int i = 0; i < CACHE_BUCKETS; i++) {
		cache_entry **pp = &cache_buckets[i];
		while (*pp) {
			cache_entry *e = *pp;
			int hit = (strcmp(e->val, path) == 0)
			       || (strncmp(e->val, path, plen) == 0 && e->val[plen] == '/');
			if (hit) {
				*pp = e->next;
				free(e->key); free(e->val); free(e);
			} else {
				pp = &(*pp)->next;
			}
		}
	}
	pthread_rwlock_unlock(&cache_lock);
}

/* ===== Directory entry cache (dir path -> on-disk entry names) ========= */

#define DIR_CACHE_BUCKETS 1024
#define DIR_ENTRIES_INIT  32

typedef struct dir_node {
	char          *dir_path;
	char         **names;
	int            count;
	int            capacity;
	struct dir_node *next;
} dir_node;

static dir_node        *dir_cache[DIR_CACHE_BUCKETS];
static pthread_rwlock_t dir_cache_lock = PTHREAD_RWLOCK_INITIALIZER;

static dir_node *dir_node_find(const char *dir_path)
{
	unsigned long b = path_hash(dir_path) % DIR_CACHE_BUCKETS;
	for (dir_node *n = dir_cache[b]; n; n = n->next)
		if (strcmp(n->dir_path, dir_path) == 0) return n;
	return NULL;
}

static dir_node *dir_node_populate(const char *dir_path)
{
	DIR *d = real_opendir(dir_path);
	if (!d) return NULL;

	dir_node *node = (dir_node *)malloc(sizeof(*node));
	if (!node) { closedir(d); return NULL; }

	node->dir_path = strdup(dir_path);
	node->capacity = DIR_ENTRIES_INIT;
	node->count    = 0;
	node->names    = (char **)malloc(node->capacity * sizeof(char *));
	if (!node->names) { free(node->dir_path); free(node); closedir(d); return NULL; }

	struct dirent *de;
	while ((de = readdir(d)) != NULL) {
		if (de->d_name[0] == '.' &&
		    (de->d_name[1] == '\0' ||
		     (de->d_name[1] == '.' && de->d_name[2] == '\0')))
			continue;
		if (node->count == node->capacity) {
			int nc = node->capacity * 2;
			char **nn = (char **)realloc(node->names, nc * sizeof(char *));
			if (!nn) break;
			node->names    = nn;
			node->capacity = nc;
		}
		node->names[node->count++] = strdup(de->d_name);
	}
	closedir(d);

	unsigned long b = path_hash(dir_path) % DIR_CACHE_BUCKETS;
	node->next   = dir_cache[b];
	dir_cache[b] = node;
	return node;
}

static int dir_cache_lookup(const char *dir_path,
                             const char *seg, size_t seg_len,
                             char out[NAME_MAX + 1])
{
	pthread_rwlock_rdlock(&dir_cache_lock);
	dir_node *node = dir_node_find(dir_path);
	if (node) {
		for (int i = 0; i < node->count; i++) {
			size_t nlen = strlen(node->names[i]);
			if (nlen == seg_len &&
			    strncasecmp(node->names[i], seg, seg_len) == 0) {
				memcpy(out, node->names[i], nlen + 1);
				pthread_rwlock_unlock(&dir_cache_lock);
				return 1;
			}
		}
		pthread_rwlock_unlock(&dir_cache_lock);
		return 0;
	}
	pthread_rwlock_unlock(&dir_cache_lock);

	pthread_rwlock_wrlock(&dir_cache_lock);
	node = dir_node_find(dir_path);
	if (!node) node = dir_node_populate(dir_path);
	int found = 0;
	if (node) {
		for (int i = 0; i < node->count; i++) {
			size_t nlen = strlen(node->names[i]);
			if (nlen == seg_len &&
			    strncasecmp(node->names[i], seg, seg_len) == 0) {
				memcpy(out, node->names[i], nlen + 1);
				found = 1;
				break;
			}
		}
	}
	pthread_rwlock_unlock(&dir_cache_lock);
	return found;
}

static void dir_cache_invalidate(const char *dir_path)
{
	if (!dir_path || !*dir_path) return;
	unsigned long b = path_hash(dir_path) % DIR_CACHE_BUCKETS;
	pthread_rwlock_wrlock(&dir_cache_lock);
	dir_node **pp = &dir_cache[b];
	while (*pp) {
		if (strcmp((*pp)->dir_path, dir_path) == 0) {
			dir_node *t = *pp; *pp = t->next;
			for (int i = 0; i < t->count; i++) free(t->names[i]);
			free(t->names); free(t->dir_path); free(t);
			break;
		}
		pp = &(*pp)->next;
	}
	pthread_rwlock_unlock(&dir_cache_lock);
}

static void dir_cache_invalidate_parent(const char *resolved_path)
{
	if (!resolved_path) return;
	char tmp[PATH_MAX];
	size_t n = strlen(resolved_path);
	if (n >= sizeof(tmp)) return;
	memcpy(tmp, resolved_path, n + 1);
	char *slash = strrchr(tmp, '/');
	if (!slash || slash == tmp) return;
	*slash = '\0';
	dir_cache_invalidate(tmp);
}

/* ===== Path resolution ================================================= */

static int exists_on_disk(const char *path)
{
	struct stat st;
	return real_lstat(path, &st) == 0;
}

static int append_segment(char *built, size_t *built_len, size_t cap,
                          const char *seg, size_t seg_len)
{
	int need_slash = (*built_len > 1);
	size_t needed = *built_len + (need_slash ? 1 : 0) + seg_len + 1;
	if (needed > cap) return 0;
	if (need_slash) built[(*built_len)++] = '/';
	memcpy(built + *built_len, seg, seg_len);
	*built_len += seg_len;
	built[*built_len] = '\0';
	return 1;
}

static char *resolve_path(const char *path)
{
	if (!path || !*path) return NULL;
	if (path[0] != '/') return strdup(path);

	if (!path_under_cwd(path)) return NULL;

	char *cached = cache_get(path);
	if (cached) {
		if (strcmp(cached, path) == 0) { free(cached); return NULL; }
		if (exists_on_disk(cached)) return cached;
		free(cached);
		cache_drop_key(path);
	}

	if (exists_on_disk(path)) { cache_set(path, path); return NULL; }

	char built[PATH_MAX];
	size_t built_len;
	const char *cursor;

	size_t prefix_len = copy_cwd_prefix(path, built, sizeof(built));
	if (prefix_len > 0) {
		built_len = prefix_len;
		cursor = path + prefix_len;
		if (*cursor == '/') cursor++;
	} else {
		built[0] = '/';
		built[1] = '\0';
		built_len = 1;
		cursor = path + 1;
	}

	const char *unresolved_tail = NULL;

	while (*cursor) {
		const char *seg = cursor;
		while (*cursor && *cursor != '/') cursor++;
		size_t seg_len = (size_t)(cursor - seg);
		if (*cursor == '/') cursor++;
		if (seg_len == 0) continue;

		size_t saved_len = built_len;
		if (!append_segment(built, &built_len, sizeof(built), seg, seg_len)) {
			built[saved_len] = '\0';
			built_len = saved_len;
			unresolved_tail = seg;
			break;
		}

		if (exists_on_disk(built)) continue;

		built_len = saved_len;
		built[built_len] = '\0';

		char matched[NAME_MAX + 1];
		if (dir_cache_lookup(built, seg, seg_len, matched)) {
			if (!append_segment(built, &built_len, sizeof(built),
			                    matched, strlen(matched))) {
				unresolved_tail = seg;
				break;
			}
		} else {
			append_segment(built, &built_len, sizeof(built), seg, seg_len);
			unresolved_tail = cursor;
			break;
		}
	}

	if (unresolved_tail && *unresolved_tail) {
		size_t tail_len = strlen(unresolved_tail);
		if (built_len + 1 + tail_len < sizeof(built)) {
			if (built[built_len - 1] != '/') built[built_len++] = '/';
			memcpy(built + built_len, unresolved_tail, tail_len);
			built_len += tail_len;
			built[built_len] = '\0';
		}
	}

	char *out = strdup(built);
	if (out && strcmp(out, path) != 0) {
		cache_set(path, out);
		LOG("resolved: %s -> %s\n", path, out);
	}
	return out;
}

static char *try_resolve(const char *path)
{
	if (!path) return NULL;
	return resolve_path(path);
}

#define USE_RESOLVED(orig, var) ((var) ? (var) : (orig))

/* ===== Hooks =========================================================== */

/* O_CREAT|O_EXCL guarantees a new file — safe to bypass resolution.
 * Plain O_CREAT may open an existing file, so resolution is still needed. */
#define IS_GUARANTEED_NEW(flags) \
	(((flags) & (O_CREAT | O_EXCL)) == (O_CREAT | O_EXCL) || \
	 ((flags) & O_TMPFILE) == O_TMPFILE)

int open(const char *path, int flags, ...)
{
	ENSURE_INIT();
	mode_t mode = 0;
	if (flags & (O_CREAT | O_TMPFILE)) {
		va_list ap; va_start(ap, flags);
		mode = (mode_t)va_arg(ap, int);
		va_end(ap);
	}
	if (IS_GUARANTEED_NEW(flags)) return real_open(path, flags, mode);
	char *r = try_resolve(path);
	int fd = real_open(USE_RESOLVED(path, r), flags, mode);
	int e = errno; free(r); errno = e;
	return fd;
}

int open64(const char *path, int flags, ...)
{
	ENSURE_INIT();
	mode_t mode = 0;
	if (flags & (O_CREAT | O_TMPFILE)) {
		va_list ap; va_start(ap, flags);
		mode = (mode_t)va_arg(ap, int);
		va_end(ap);
	}
	if (IS_GUARANTEED_NEW(flags)) return real_open64(path, flags, mode);
	char *r = try_resolve(path);
	int fd = real_open64(USE_RESOLVED(path, r), flags, mode);
	int e = errno; free(r); errno = e;
	return fd;
}

int openat(int dirfd, const char *path, int flags, ...)
{
	ENSURE_INIT();
	mode_t mode = 0;
	if (flags & (O_CREAT | O_TMPFILE)) {
		va_list ap; va_start(ap, flags);
		mode = (mode_t)va_arg(ap, int);
		va_end(ap);
	}
	if (IS_GUARANTEED_NEW(flags)) return real_openat(dirfd, path, flags, mode);
	char *r = (path && path[0] == '/') ? resolve_path(path) : NULL;
	int fd = real_openat(dirfd, USE_RESOLVED(path, r), flags, mode);
	int e = errno; free(r); errno = e;
	return fd;
}

int openat64(int dirfd, const char *path, int flags, ...)
{
	ENSURE_INIT();
	mode_t mode = 0;
	if (flags & (O_CREAT | O_TMPFILE)) {
		va_list ap; va_start(ap, flags);
		mode = (mode_t)va_arg(ap, int);
		va_end(ap);
	}
	if (IS_GUARANTEED_NEW(flags)) return real_openat64(dirfd, path, flags, mode);
	char *r = (path && path[0] == '/') ? resolve_path(path) : NULL;
	int fd = real_openat64(dirfd, USE_RESOLVED(path, r), flags, mode);
	int e = errno; free(r); errno = e;
	return fd;
}

FILE *fopen(const char *path, const char *mode)
{
	ENSURE_INIT();
	char *r = try_resolve(path);
	FILE *f = real_fopen(USE_RESOLVED(path, r), mode);
	int e = errno; free(r); errno = e;
	return f;
}

FILE *fopen64(const char *path, const char *mode)
{
	ENSURE_INIT();
	char *r = try_resolve(path);
	FILE *f = real_fopen64(USE_RESOLVED(path, r), mode);
	int e = errno; free(r); errno = e;
	return f;
}

FILE *freopen(const char *path, const char *mode, FILE *stream)
{
	ENSURE_INIT();
	char *r = try_resolve(path);
	FILE *f = real_freopen(USE_RESOLVED(path, r), mode, stream);
	int e = errno; free(r); errno = e;
	return f;
}

int stat(const char *path, struct stat *buf)
{
	ENSURE_INIT();
	char *r = try_resolve(path);
	int rc = real_stat(USE_RESOLVED(path, r), buf);
	int e = errno; free(r); errno = e;
	return rc;
}

int stat64(const char *path, struct stat64 *buf)
{
	ENSURE_INIT();
	char *r = try_resolve(path);
	int rc = real_stat64(USE_RESOLVED(path, r), buf);
	int e = errno; free(r); errno = e;
	return rc;
}

int lstat(const char *path, struct stat *buf)
{
	ENSURE_INIT();
	char *r = try_resolve(path);
	int rc = real_lstat(USE_RESOLVED(path, r), buf);
	int e = errno; free(r); errno = e;
	return rc;
}

/* .NET libSystem.Native.so imports lstat64@GLIBC_2.33 specifically. */
int lstat64(const char *path, struct stat64 *buf)
{
	ENSURE_INIT();
	char *r = try_resolve(path);
	int rc = real_lstat64(USE_RESOLVED(path, r), buf);
	int e = errno; free(r); errno = e;
	return rc;
}

/* glibc's public symbol is fstatat; strace shows the kernel name (newfstatat). */
int fstatat(int dirfd, const char *path, struct stat *buf, int flags)
{
	ENSURE_INIT();
	char *r = (path && path[0] == '/') ? resolve_path(path) : NULL;
	int rc = real_fstatat(dirfd, USE_RESOLVED(path, r), buf, flags);
	int e = errno; free(r); errno = e;
	return rc;
}

int fstatat64(int dirfd, const char *path, struct stat64 *buf, int flags)
{
	ENSURE_INIT();
	char *r = (path && path[0] == '/') ? resolve_path(path) : NULL;
	int rc = real_fstatat64(dirfd, USE_RESOLVED(path, r), buf, flags);
	int e = errno; free(r); errno = e;
	return rc;
}

/* .NET coreclr uses statx on modern kernels for file-existence checks. */
int statx(int dirfd, const char *path, int flags, unsigned int mask, struct statx *stx)
{
	ENSURE_INIT();
	char *r = (path && path[0] == '/') ? resolve_path(path) : NULL;
	int rc = real_statx(dirfd, USE_RESOLVED(path, r), flags, mask, stx);
	int e = errno; free(r); errno = e;
	return rc;
}

int access(const char *path, int mode)
{
	ENSURE_INIT();
	char *r = try_resolve(path);
	int rc = real_access(USE_RESOLVED(path, r), mode);
	int e = errno; free(r); errno = e;
	return rc;
}

int faccessat(int dirfd, const char *path, int mode, int flags)
{
	ENSURE_INIT();
	char *r = (path && path[0] == '/') ? resolve_path(path) : NULL;
	int rc = real_faccessat(dirfd, USE_RESOLVED(path, r), mode, flags);
	int e = errno; free(r); errno = e;
	return rc;
}

DIR *opendir(const char *path)
{
	ENSURE_INIT();
	char *r = try_resolve(path);
	DIR *d = real_opendir(USE_RESOLVED(path, r));
	int e = errno; free(r); errno = e;
	return d;
}

int unlink(const char *path)
{
	ENSURE_INIT();
	char *r = try_resolve(path);
	const char *target = USE_RESOLVED(path, r);
	int rc = real_unlink(target);
	int e = errno;
	if (rc == 0) {
		cache_drop_subtree(target);
		dir_cache_invalidate_parent(target);
	}
	free(r);
	errno = e;
	return rc;
}

int unlinkat(int dirfd, const char *path, int flags)
{
	ENSURE_INIT();
	char *r = (path && path[0] == '/') ? resolve_path(path) : NULL;
	const char *target = USE_RESOLVED(path, r);
	int rc = real_unlinkat(dirfd, target, flags);
	int e = errno;
	if (rc == 0 && r) {
		cache_drop_subtree(target);
		dir_cache_invalidate_parent(target);
	}
	free(r);
	errno = e;
	return rc;
}

int rmdir(const char *path)
{
	ENSURE_INIT();
	char *r = try_resolve(path);
	const char *target = USE_RESOLVED(path, r);
	int rc = real_rmdir(target);
	int e = errno;
	if (rc == 0) {
		cache_drop_subtree(target);
		dir_cache_invalidate(target);
		dir_cache_invalidate_parent(target);
	}
	free(r);
	errno = e;
	return rc;
}

int rename(const char *oldp, const char *newp)
{
	ENSURE_INIT();
	char *ro = try_resolve(oldp);
	char *rn = try_resolve(newp);
	const char *src = USE_RESOLVED(oldp, ro);
	const char *dst = USE_RESOLVED(newp, rn);
	int rc = real_rename(src, dst);
	int e = errno;
	if (rc == 0) {
		cache_drop_subtree(src);
		dir_cache_invalidate_parent(src);
		dir_cache_invalidate_parent(dst);
	}
	free(ro); free(rn);
	errno = e;
	return rc;
}

int renameat(int olddirfd, const char *oldp, int newdirfd, const char *newp)
{
	ENSURE_INIT();
	char *ro = (oldp && oldp[0] == '/') ? resolve_path(oldp) : NULL;
	char *rn = (newp && newp[0] == '/') ? resolve_path(newp) : NULL;
	const char *src = USE_RESOLVED(oldp, ro);
	const char *dst = USE_RESOLVED(newp, rn);
	int rc = real_renameat(olddirfd, src, newdirfd, dst);
	int e = errno;
	if (rc == 0) {
		if (ro) { cache_drop_subtree(src); dir_cache_invalidate_parent(src); }
		if (rn)   dir_cache_invalidate_parent(dst);
	}
	free(ro); free(rn);
	errno = e;
	return rc;
}

/* New directories don't exist yet — bypass resolution. */
int mkdir(const char *path, mode_t mode)
{
	ENSURE_INIT();
	return real_mkdir(path, mode);
}

int mkdirat(int dirfd, const char *path, mode_t mode)
{
	ENSURE_INIT();
	return real_mkdirat(dirfd, path, mode);
}

char *realpath(const char *path, char *resolved)
{
	ENSURE_INIT();
	char *r = try_resolve(path);
	char *out = real_realpath(USE_RESOLVED(path, r), resolved);
	int e = errno; free(r); errno = e;
	return out;
}

int chmod(const char *path, mode_t mode)
{
	ENSURE_INIT();
	char *r = try_resolve(path);
	int rc = real_chmod(USE_RESOLVED(path, r), mode);
	int e = errno; free(r); errno = e;
	return rc;
}

int chdir(const char *path)
{
	ENSURE_INIT();
	char *r = try_resolve(path);
	int rc = real_chdir(USE_RESOLVED(path, r));
	int e = errno;
	if (rc == 0) capture_cwd();
	free(r);
	errno = e;
	return rc;
}
