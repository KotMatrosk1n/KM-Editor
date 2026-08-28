// SPDX-License-Identifier: GPL-3.0-only

.section .text.km_sv_registration_trampoline, "ax", %progbits
.align 2
.global km_sv_registration_trampoline
.hidden km_sv_registration_trampoline
.type km_sv_registration_trampoline, %function
km_sv_registration_trampoline:
    // Replay the four retail instructions displaced at main + 0x004C550.
    stp x29, x30, [sp, #-0x20]!
    str x19, [sp, #0x10]
    mov x29, sp
    mov x8, #0x800000000000

    // Continue at main + 0x004C560. The retail epilogue returns to the
    // caller of this trampoline, after which the hook can add KM globals.
    adrp x17, g_km_sv_registration_resume
    add x17, x17, :lo12:g_km_sv_registration_resume
    ldr x17, [x17]
    br x17
.size km_sv_registration_trampoline, . - km_sv_registration_trampoline
