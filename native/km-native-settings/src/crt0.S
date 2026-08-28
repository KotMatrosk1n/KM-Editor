// SPDX-License-Identifier: GPL-3.0-only

.section .text.crt0,"ax",%progbits
.global __module_start
.hidden __module_start
.type __module_start,%function
.hidden km_runtime_start
.hidden km_relocate_self

__module_start:
    b .Lstartup
    .word __nx_mod0 - __module_start
    .space 0x78

.Lstartup:
    stp x29, x30, [sp, #-0x30]!
    mov x29, sp
    stp x0, x1, [sp, #0x10]

    adrp x8, __bss_start__
    add x8, x8, #:lo12:__bss_start__
    adrp x9, __bss_end__
    add x9, x9, #:lo12:__bss_end__
.Lbss_loop:
    cmp x8, x9
    b.hs .Lbss_done
    str xzr, [x8], #8
    b .Lbss_loop
.Lbss_done:

    adr x0, __module_start
    adr x2, __nx_mod0
    ldrsw x1, [x2, #4]
    add x1, x1, x2
    bl km_relocate_self

    ldp x0, x1, [sp, #0x10]
    bl km_runtime_start
    ldp x29, x30, [sp], #0x30
    ret

.size __module_start, . - __module_start

.section .rodata.mod0,"a",%progbits
.align 2
.global __nx_mod0
.hidden __nx_mod0
__nx_mod0:
    .ascii "MOD0"
    .word __dynamic_start__ - __nx_mod0
    .word __bss_start__ - __nx_mod0
    .word __bss_end__ - __nx_mod0
    .word 0
    .word 0
    .word __nx_module_runtime - __nx_mod0
