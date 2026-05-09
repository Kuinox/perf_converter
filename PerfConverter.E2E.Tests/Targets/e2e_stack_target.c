#include <stdint.h>
#include <stdio.h>

volatile uint64_t e2e_sink;

__attribute__((noinline, used))
uint64_t e2e_leaf(uint64_t value)
{
    asm volatile("" ::: "memory");
    return (value * 1664525u) + 1013904223u;
}

__attribute__((noinline, used))
uint64_t e2e_mid(uint64_t value)
{
    return e2e_leaf(value) ^ 0x9e3779b97f4a7c15ull;
}

__attribute__((noinline, used))
uint64_t e2e_root(uint64_t iterations)
{
    uint64_t sum = 0;
    for (uint64_t i = 0; i < iterations; i++)
        sum += e2e_mid(i);
    return sum;
}

int main(void)
{
    e2e_sink = e2e_root(20000);
    printf("e2e_sink=%llu\n", (unsigned long long)e2e_sink);
    return e2e_sink == 0;
}
