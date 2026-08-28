// SPDX-License-Identifier: GPL-3.0-only

.section .text,"ax",%progbits
.align 2

.global km_swsh_registration_original
.hidden km_swsh_registration_original
.type km_swsh_registration_original,%function
km_swsh_registration_original:
    str x19, [sp, #-0x20]!
    stp x29, x30, [sp, #0x10]
    add x29, sp, #0x10
    mov x19, x0
    adrp x17, km_swsh_registration_continue
    ldr x17, [x17, #:lo12:km_swsh_registration_continue]
    br x17
.size km_swsh_registration_original, . - km_swsh_registration_original

// Replaces the retail LDR W8, [SP] at the final battle/catch EXP scaling
// point. Every register and NZCV bit except the intended W8 result is
// preserved. The single acquire-load is also the readiness gate: zero is
// exact retail, while a published snapshot carries the 0-500% basis points.
.global km_swsh_rate_bridge
.hidden km_swsh_rate_bridge
.type km_swsh_rate_bridge,%function
km_swsh_rate_bridge:
    ldr w8, [sp]
    stp x9, x10, [sp, #-0x20]!
    stp x11, x12, [sp, #0x10]
    mrs x11, nzcv
    adrp x9, km_swsh_effective_snapshot
    add x9, x9, #:lo12:km_swsh_effective_snapshot
    ldar x9, [x9]
    cbz x9, .Lswsh_rate_restore
    ubfx x10, x9, #9, #32
    cbz x10, .Lswsh_rate_zero
    mov w12, w8
    umull x8, w8, w10
    mov w9, #10000
    udiv x8, x8, x9
    mov w9, #-1
    cmp x8, x9
    csel x8, x8, x9, ls
    cbz w12, .Lswsh_rate_zero
    cbnz w8, .Lswsh_rate_restore
    mov w8, #1
    b .Lswsh_rate_restore
.Lswsh_rate_zero:
    mov w8, wzr
.Lswsh_rate_restore:
    msr nzcv, x11
    ldp x11, x12, [sp, #0x10]
    ldp x9, x10, [sp], #0x20
    ret
.size km_swsh_rate_bridge, . - km_swsh_rate_bridge

// The cap bridge runs at the verified award function entry. Its C++ helper
// returns either the original W2 award or the bounded value from one packed
// snapshot. The overwritten retail prologue executes before resuming at +4.
.global km_swsh_level_cap_bridge
.hidden km_swsh_level_cap_bridge
.type km_swsh_level_cap_bridge,%function
km_swsh_level_cap_bridge:
    sub sp, sp, #0x130
    stp x0, x1, [sp]
    stp x2, x3, [sp, #0x10]
    stp x4, x5, [sp, #0x20]
    stp x6, x7, [sp, #0x30]
    stp x8, x9, [sp, #0x40]
    stp x10, x11, [sp, #0x50]
    stp x12, x13, [sp, #0x60]
    stp x14, x15, [sp, #0x70]
    stp x16, x17, [sp, #0x80]
    stp x18, x30, [sp, #0x90]
    mrs x9, nzcv
    str x9, [sp, #0xa0]
    stp q0, q1, [sp, #0xb0]
    stp q2, q3, [sp, #0xd0]
    stp q4, q5, [sp, #0xf0]
    stp q6, q7, [sp, #0x110]
    bl km_swsh_apply_level_cap
    uxtw x0, w0
    str x0, [sp, #0x10]
    ldp q6, q7, [sp, #0x110]
    ldp q4, q5, [sp, #0xf0]
    ldp q2, q3, [sp, #0xd0]
    ldp q0, q1, [sp, #0xb0]
    ldr x9, [sp, #0xa0]
    msr nzcv, x9
    ldp x18, x30, [sp, #0x90]
    ldp x16, x17, [sp, #0x80]
    ldp x14, x15, [sp, #0x70]
    ldp x12, x13, [sp, #0x60]
    ldp x10, x11, [sp, #0x50]
    ldp x8, x9, [sp, #0x40]
    ldp x6, x7, [sp, #0x30]
    ldp x4, x5, [sp, #0x20]
    ldp x2, x3, [sp, #0x10]
    ldp x0, x1, [sp]
    add sp, sp, #0x130
    str x21, [sp, #-0x30]!
    adrp x17, km_swsh_level_cap_continue
    ldr x17, [x17, #:lo12:km_swsh_level_cap_continue]
    br x17
.size km_swsh_level_cap_bridge, . - km_swsh_level_cap_bridge
