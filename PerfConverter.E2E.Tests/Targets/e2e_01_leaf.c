#include <stdint.h>
#include <stdio.h>

volatile uint64_t e2e_sink;

__attribute__((noinline, used))
uint64_t e2e_leaf(uint64_t value)
{
    asm volatile("" ::: "memory");
    e2e_sink += value;
    return value + 1;
}

int main(void)
{
    uint64_t sum = 0;
    for (uint64_t i = 0; i < 20000; i++)
        sum += e2e_leaf(i);

    e2e_sink = sum;
    printf("e2e_sink=%llu\n", (unsigned long long)e2e_sink);
    return e2e_sink == 0;
}
