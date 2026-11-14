@ --- Linux ARM32 "Hello World" Syscall ---
.global _start

.data
msg:
    .ascii "Hello, ARM32 Linux World!\n"

.text
_start:
    @ sys_write(stdout=1, buf=msg, count=26)
    mov r0, #1          @ 1 = stdout
    ldr r1, =msg        @ load address of msg
    mov r2, #26         @ string length
    mov r7, #4          @ syscall #4: sys_write
    svc 0               @ trigger supervisor call

    @ sys_exit(status=0)
    mov r0, #0          @ exit status code
    mov r7, #1          @ syscall #1: sys_exit
    svc 0