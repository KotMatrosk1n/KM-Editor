// SPDX-License-Identifier: GPL-3.0-only
.section .text.km_original_effectiveness,"ax",%progbits
.global km_original_effectiveness
.hidden km_original_effectiveness
.type km_original_effectiveness,%function
km_original_effectiveness:
    // Replay the displaced instruction, then resume the unmodified function.
    str x27, [sp, #-0x60]!
    adrp x16, km_effectiveness_resume
    ldr x16, [x16, :lo12:km_effectiveness_resume]
    br x16
.size km_original_effectiveness, .-km_original_effectiveness

.macro property_trampoline name, resume, transform=0
.section .text.\name,"ax",%progbits
.global \name
.hidden \name
.type \name,%function
\name:
.if \transform
    sub w8, w0, #0xd1
.else
    and w8, w0, #0xffff
.endif
    adrp x16, \resume
    ldr x16, [x16, :lo12:\resume]
    br x16
.size \name, .-\name
.endm
property_trampoline km_original_copy_property, km_copy_property_resume
property_trampoline km_original_suppress_property, km_suppress_property_resume
property_trampoline km_original_swap_property, km_swap_property_resume
property_trampoline km_original_transform_property, km_transform_property_resume, 1

.macro submission_trampoline name, resume
.section .text.\name,"ax",%progbits
.global \name
.hidden \name
.type \name,%function
\name:
    str x19, [sp, #-0x20]!
    adrp x16, \resume
    ldr x16, [x16, :lo12:\resume]
    br x16
.size \name, .-\name
.endm
submission_trampoline km_original_status_submission, km_status_submission_resume
submission_trampoline km_original_rank_submission, km_rank_submission_resume

.section .text.km_original_damage_weather,"ax",%progbits
.global km_original_damage_weather
.hidden km_original_damage_weather
.type km_original_damage_weather,%function
km_original_damage_weather:
    sub sp, sp, #0x80
    adrp x16, km_damage_weather_resume
    ldr x16, [x16, :lo12:km_damage_weather_resume]
    br x16
.size km_original_damage_weather, .-km_original_damage_weather

.macro commander_trampoline name, resume, switch=0
.section .text.\name,"ax",%progbits
.global \name
.hidden \name
.type \name,%function
\name:
.if \switch
    stp x22, x21, [sp, #-0x30]!
.else
    stp x24, x23, [sp, #-0x40]!
.endif
    adrp x16, \resume
    ldr x16, [x16, :lo12:\resume]
    br x16
.size \name, .-\name
.endm
commander_trampoline km_original_dispatch, km_dispatch_resume
commander_trampoline km_original_invulnerability, km_invulnerability_resume
commander_trampoline km_original_switch, km_switch_resume, 1

.section .text.km_original_automatic_command,"ax",%progbits
.global km_original_automatic_command
.hidden km_original_automatic_command
.type km_original_automatic_command,%function
km_original_automatic_command:
    sub sp, sp, #0x40
    adrp x16, km_automatic_command_resume
    ldr x16, [x16, :lo12:km_automatic_command_resume]
    br x16
.size km_original_automatic_command, .-km_original_automatic_command
