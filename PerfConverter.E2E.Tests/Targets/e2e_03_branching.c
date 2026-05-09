#include <stdint.h>
#include <stdio.h>

volatile uint64_t e2e_sink;

__attribute__((noinline, used))
uint64_t e2e_left_leaf(uint64_t value)
{
    asm volatile("" ::: "memory");
    e2e_sink += value;
    return value + 1;
}

__attribute__((noinline, used))
uint64_t e2e_right_leaf(uint64_t value)
{
    asm volatile("" ::: "memory");
    e2e_sink += value * 2;
    return value + 2;
}

__attribute__((noinline, used))
uint64_t e2e_branch(uint64_t value)
{
    if ((value & 1) == 0)
        return e2e_left_leaf(value);

    return e2e_right_leaf(value);
}

__attribute__((noinline, used))
uint64_t e2e_root(uint64_t iterations)
{
    uint64_t sum = 0;
    for (uint64_t i = 0; i < iterations; i++)
        sum += e2e_branch(i);
    return sum;
}

int main(void)
{
    e2e_sink = e2e_root(20000);
    printf("e2e_sink=%llu\n", (unsigned long long)e2e_sink);
    return e2e_sink == 0;
}
