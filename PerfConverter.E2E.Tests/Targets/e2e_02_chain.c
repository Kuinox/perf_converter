#include <stdint.h>

volatile uint64_t e2e_sink;

#define E2E_SPIN(seed, rounds) \
    do { \
        for (volatile uint64_t spin = 0; spin < (rounds); spin++) { \
            e2e_sink += ((seed) + spin) & 1; \
            asm volatile("" ::: "memory"); \
        } \
    } while (0)

__attribute__((noinline, used))
uint64_t e2e_leaf(uint64_t value)
{
    E2E_SPIN(value, 80);
    e2e_sink += value;
    return value + 1;
}

__attribute__((noinline, used))
uint64_t e2e_mid(uint64_t value)
{
    E2E_SPIN(value, 30);
    return e2e_leaf(value) + 1;
}

__attribute__((noinline, used))
uint64_t e2e_root(uint64_t iterations)
{
    uint64_t sum = 0;
    for (uint64_t i = 0; i < iterations; i++)
    {
        E2E_SPIN(i, 10);
        sum += e2e_mid(i);
    }
    return sum;
}

int main(void)
{
    E2E_SPIN(1, 100000);
    e2e_sink = e2e_root(1000);
    return e2e_sink == 0;
}
