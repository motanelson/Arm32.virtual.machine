@ --- Fibonacci with Stack Frame (PUSH/POP) ---
.global _start

.text
_start:
    mov r0, #7          @ compute fib(7) = 13
    bl fib
    mov r7, #1          @ sys_exit with result in r0
    svc 0

fib:
    cmp r0, #1
    ble fib_base        @ base case: if n <= 1 return n

    push {r4, lr}
    mov r4, r0          @ preserve n
    sub r0, r4, #1      @ n - 1
    bl fib

    push {r0}           @ store fib(n-1)
    sub r0, r4, #2      @ n - 2
    bl fib
    mov r1, r0          @ fib(n-2)
    pop {r0}            @ fib(n-1)

    add r0, r0, r1      @ fib(n-1) + fib(n-2)
    pop {r4, lr}
    bx lr

fib_base:
    bx lr