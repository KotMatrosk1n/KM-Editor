// SPDX-License-Identifier: GPL-3.0-only

.section .text.km_za_202_rate_bridge,"ax",%progbits
.global km_za_202_rate_bridge
.hidden km_za_202_rate_bridge
.type km_za_202_rate_bridge,%function
.align 2
km_za_202_rate_bridge:
    // Preserve every caller-saved register, SIMD argument/result register,
    // condition flag, and LR which can be live at the two retail accumulation
    // sites. The C++ callback owns only the resulting W8 accumulator value.
    sub sp, sp, #0x130
    stp x0, x1, [sp, #0x00]
    stp x2, x3, [sp, #0x10]
    stp x4, x5, [sp, #0x20]
    stp x6, x7, [sp, #0x30]
    stp x8, x9, [sp, #0x40]
    stp x10, x11, [sp, #0x50]
    stp x12, x13, [sp, #0x60]
    stp x14, x15, [sp, #0x70]
    stp x16, x17, [sp, #0x80]
    str x18, [sp, #0x90]
    str x30, [sp, #0x98]
    mrs x9, nzcv
    str x9, [sp, #0xa0]
    stp q0, q1, [sp, #0xb0]
    stp q2, q3, [sp, #0xd0]
    stp q4, q5, [sp, #0xf0]
    stp q6, q7, [sp, #0x110]

    mov x0, x19
    mov x1, x21
    mov w2, w23
    bl km_za_202_rate_callback

    ldp q6, q7, [sp, #0x110]
    ldp q4, q5, [sp, #0xf0]
    ldp q2, q3, [sp, #0xd0]
    ldp q0, q1, [sp, #0xb0]
    ldr x9, [sp, #0xa0]
    msr nzcv, x9
    ldr x30, [sp, #0x98]
    ldr x18, [sp, #0x90]
    ldp x16, x17, [sp, #0x80]
    ldp x14, x15, [sp, #0x70]
    ldp x12, x13, [sp, #0x60]
    ldp x10, x11, [sp, #0x50]
    ldp x8, x9, [sp, #0x40]
    ldp x6, x7, [sp, #0x30]
    ldp x4, x5, [sp, #0x20]
    ldp x2, x3, [sp, #0x10]
    ldp x0, x1, [sp, #0x00]
    // The callback has already written the bounded final accumulator.
    ldr w8, [x21, #0x18]
    add sp, sp, #0x130
    ret
.size km_za_202_rate_bridge, . - km_za_202_rate_bridge

.section .text.km_za_202_back_original,"ax",%progbits
.global km_za_202_back_original
.hidden km_za_202_back_original
.type km_za_202_back_original,%function
.align 2
km_za_202_back_original:
    // Replay the exact four overwritten instructions at main+0x2C8219C,
    // then resume inside the unmodified retail body.
    sub sp, sp, #0x70
    stp x29, x30, [sp, #0x50]
    stp x20, x19, [sp, #0x60]
    add x29, sp, #0x50
    adrp x17, km_za_202_main_base
    ldr x17, [x17, :lo12:km_za_202_main_base]
    ldr x16, 1f
    add x17, x17, x16
    br x17
    .align 3
1:
    .xword 0x02C821AC
.size km_za_202_back_original, . - km_za_202_back_original

.section .text.km_za_202_sleep_thread,"ax",%progbits
.global km_za_202_sleep_thread
.hidden km_za_202_sleep_thread
.type km_za_202_sleep_thread,%function
.align 2
km_za_202_sleep_thread:
    svc #0x0b
    ret
.size km_za_202_sleep_thread, . - km_za_202_sleep_thread
