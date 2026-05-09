#include <stdint.h>
#include <stdio.h>

volatile uint64_t e2e_sink;

__attribute__((noinline, used))
uint64_t e2e_recursive(uint64_t value, uint64_t depth)
{
    asm volatile("" ::: "memory");
    if (depth == 0) {
        e2e_sink += value;
        return value + 1;
    }

    return e2e_recursive(value + 1, depth - 1) + 1;
}

__attribute__((noinline, used))
uint64_t e2e_root(uint64_t iterations)
{
    uint64_t sum = 0;
    for (uint64_t i = 0; i < iterations; i++)
        sum += e2e_recursive(i, 3);
    return sum;
}

int main(void)
{
    e2e_sink = e2e_root(12000);
    printf("e2e_sink=%llu\n", (unsigned long long)e2e_sink);
    return e2e_sink == 0;
}
