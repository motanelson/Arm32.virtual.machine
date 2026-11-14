@ --- Countdown & Loop Syscall Demo ---
.global _start

.data
tick:
    .ascii "* "

.text
_start:
    mov r4, #5          @ count from 5 down to 1

loop:
    @ sys_write(1, tick, 2)
    mov r0, #1
    ldr r1, =tick
    mov r2, #2
    mov r7, #4
    svc 0

    @ Decrement counter and update flags
    subs r4, r4, #1     @ update Z flag in CPSR
    bne loop            @ repeat if r4 != 0

exit:
    mov r0, #42         @ return 42
    mov r7, #1          @ syscall #1: sys_exit
    svc 0