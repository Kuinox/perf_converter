struct perf_dlfilter_al;

struct perf_dlfilter_fns_struct {
    const struct perf_dlfilter_al *(*resolve_ip)(void *ctx);
    const struct perf_dlfilter_al *(*resolve_addr)(void *ctx);
    char **(*args)(void *ctx, int *dlargc);
    int (*resolve_address)(void *ctx, unsigned long long address, struct perf_dlfilter_al *al);
    const unsigned char *(*insn)(void *ctx, unsigned int *length);
    const char *(*srcline)(void *ctx, unsigned int *line_number);
    struct perf_event_attr *(*attr)(void *ctx);
    int (*object_code)(void *ctx, unsigned long long ip, void *buf, unsigned int len);
    void (*al_cleanup)(void *ctx, struct perf_dlfilter_al *al);
    void *(*reserved[119])(void *);
};

__attribute__((visibility("default"), used, externally_visible))
struct perf_dlfilter_fns_struct perf_dlfilter_fns = {0};

// Provide a function returning its address. This will be easier to call from C#.
__attribute__((visibility("default"), used, externally_visible))
struct perf_dlfilter_fns_struct* get_perf_dlfilter_fns()
{
    return &perf_dlfilter_fns;
}

__attribute__((visibility("default")))
void dummy_force_export() {
    volatile void* p = &perf_dlfilter_fns;
}