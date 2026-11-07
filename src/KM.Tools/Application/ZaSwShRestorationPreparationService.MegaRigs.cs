// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;

namespace KM.Tools.Application;

public sealed partial class ZaSwShRestorationPreparationService
{
    private sealed partial class Preparation
    {
        // Explicit shared anatomy, qualified against both catalog rigs and every
        // missing authored action. Mega-only branches retain their own bind pose.
        private static string[]? ExtendedMegaBaseCampAnatomy(int species, int form) => (species, form) switch
        {
            (71, 1) => [
                "origin", "waist", "spine", "neck", "head", "left_jaw_a",
                "right_jaw_a", "left_upper_eyelid_a", "right_upper_eyelid_a", "left_upper_eyelid_b", "right_upper_eyelid_b", "left_upper_eyelid_c",
                "right_upper_eyelid_c", "left_upper_eyelid_d", "right_upper_eyelid_d", "left_upper_eyelid_e", "right_upper_eyelid_e", "left_lower_eyelid_a",
                "right_lower_eyelid_a", "left_lower_eyelid_b", "right_lower_eyelid_b", "left_lower_eyelid_c", "right_lower_eyelid_c", "left_jaw_b",
                "right_jaw_b", "left_jaw_c", "left_teeth", "right_jaw_c", "right_teeth", "left_upper_eyelid_f",
                "right_upper_eyelid_f", "left_upper_eyelid_g", "right_upper_eyelid_g", "left_upper_eyelid_h", "right_upper_eyelid_h", "left_upper_eyelid_i",
                "right_upper_eyelid_i", "left_upper_eyelid_j", "right_upper_eyelid_j", "left_lower_eyelid_d", "right_lower_eyelid_d", "left_lower_eyelid_e",
                "right_lower_eyelid_e", "left_lower_eyelid_f", "right_lower_eyelid_f", "feeler_a", "hips", "left_feeler_a",
                "right_feeler_a"],
            (94, 1) => [
                "origin", "waist", "spine", "head", "jaw", "left_arm_01",
                "left_arm_02", "left_hand", "right_arm_01", "right_arm_02", "right_hand", "left_upper_eyelid_a_01",
                "left_upper_eyelid_a_02", "left_upper_eyelid_b_01", "left_upper_eyelid_b_02", "left_upper_eyelid_c_01", "left_upper_eyelid_c_02", "left_upper_eyelid_d_01",
                "left_upper_eyelid_d_02", "left_upper_eyelid_e_01", "left_upper_eyelid_e_02", "left_upper_eyelid_f_01", "left_upper_eyelid_f_02", "right_upper_eyelid_a_01",
                "right_upper_eyelid_a_02", "right_upper_eyelid_b_01", "right_upper_eyelid_b_02", "right_upper_eyelid_c_01", "right_upper_eyelid_c_02", "right_upper_eyelid_d_01",
                "right_upper_eyelid_d_02", "right_upper_eyelid_e_01", "right_upper_eyelid_e_02", "right_upper_eyelid_f_01", "right_upper_eyelid_f_02", "left_lower_eyelid_a_01",
                "left_lower_eyelid_a_02", "right_lower_eyelid_a_01", "right_lower_eyelid_a_02", "hips", "left_leg_01", "left_leg_02",
                "left_foot", "left_foot_index", "right_leg_01", "right_leg_02", "right_foot", "right_foot_index"],
            (121, 1) => [
                "origin", "waist", "left_arm_01", "left_arm_02", "left_hand", "right_arm_01",
                "right_arm_02", "right_hand", "left_leg_01", "left_leg_02", "left_foot", "right_leg_01",
                "right_leg_02", "right_foot", "left_tail_a", "right_tail_a", "left_tail_b", "right_tail_b"],
            (142, 1) => [
                "origin", "waist", "spine_01", "spine_02", "head", "left_upper_eyelid_a",
                "left_upper_eyelid_a_sub", "left_upper_eyelid_b", "left_upper_eyelid_b_sub", "left_upper_eyelid_c", "left_upper_eyelid_c_sub", "left_upper_eyelid_d",
                "left_upper_eyelid_d_sub", "left_upper_eyelid_e", "left_upper_eyelid_e_sub", "left_upper_eyelid_f", "left_upper_eyelid_f_sub", "left_upper_eyelid_g",
                "left_upper_eyelid_g_sub", "left_upper_eyelid_h", "left_upper_eyelid_h_sub", "left_upper_eyelid_i", "left_upper_eyelid_i_sub", "left_upper_eyelid_j",
                "left_upper_eyelid_j_sub", "left_upper_eyelid_k", "left_upper_eyelid_k_sub", "left_lower_eyelid_a", "left_lower_eyelid_a_sub", "left_lower_eyelid_b",
                "left_lower_eyelid_b_sub", "left_lower_eyelid_c", "left_lower_eyelid_c_sub", "left_lower_eyelid_d", "left_lower_eyelid_d_sub", "left_lower_eyelid_e",
                "left_lower_eyelid_e_sub", "left_lower_eyelid_f", "left_lower_eyelid_f_sub", "left_lower_eyelid_g", "left_lower_eyelid_g_sub", "left_lower_eyelid_h",
                "left_lower_eyelid_h_sub", "right_upper_eyelid_a", "right_upper_eyelid_a_sub", "right_upper_eyelid_b", "right_upper_eyelid_b_sub", "right_upper_eyelid_c",
                "right_upper_eyelid_c_sub", "right_upper_eyelid_d", "right_upper_eyelid_d_sub", "right_upper_eyelid_e", "right_upper_eyelid_e_sub", "right_upper_eyelid_f",
                "right_upper_eyelid_f_sub", "right_upper_eyelid_g", "right_upper_eyelid_g_sub", "right_upper_eyelid_h", "right_upper_eyelid_h_sub", "right_upper_eyelid_i",
                "right_upper_eyelid_i_sub", "right_upper_eyelid_j", "right_upper_eyelid_j_sub", "right_upper_eyelid_k", "right_upper_eyelid_k_sub", "right_lower_eyelid_a",
                "right_lower_eyelid_a_sub", "right_lower_eyelid_b", "right_lower_eyelid_b_sub", "right_lower_eyelid_c", "right_lower_eyelid_c_sub", "right_lower_eyelid_d",
                "right_lower_eyelid_d_sub", "right_lower_eyelid_e", "right_lower_eyelid_e_sub", "right_lower_eyelid_f", "right_lower_eyelid_f_sub", "right_lower_eyelid_g",
                "right_lower_eyelid_g_sub", "right_lower_eyelid_h", "right_lower_eyelid_h_sub", "jaw", "tongue_01", "tongue_02",
                "left_shoulder", "right_shoulder", "hips", "tail_01", "tail_02", "tail_03",
                "tail_04", "tail_05", "tail_06", "left_leg_01", "left_leg_02", "left_foot",
                "left_foot_index", "left_foot_middle", "left_foot_thumb", "right_leg_01", "right_leg_02", "right_foot",
                "right_foot_index", "right_foot_middle", "right_foot_thumb"],
            (149, 1) => [
                "origin", "waist", "spine_01", "spine_02", "neck", "head",
                "left_upper_eyelid_c_01", "left_upper_eyelid_c_02", "right_upper_eyelid_c_01", "right_upper_eyelid_c_02", "left_upper_eyelid_d_01", "left_upper_eyelid_d_02",
                "right_upper_eyelid_d_01", "right_upper_eyelid_d_02", "left_lower_eyelid_a", "right_lower_eyelid_a", "left_lower_eyelid_b", "right_lower_eyelid_b",
                "left_lower_eyelid_c", "right_lower_eyelid_c", "jaw", "left_shoulder", "left_arm_01", "left_arm_02",
                "left_hand", "left_index", "left_middle", "left_ring", "right_shoulder", "right_arm_01",
                "right_arm_02", "right_hand", "right_index", "right_middle", "right_ring", "hips",
                "left_leg_01", "left_leg_02", "left_foot", "left_foot_index", "right_leg_01", "right_leg_02",
                "right_foot", "right_foot_index", "tail_01", "tail_02", "tail_03", "tail_04",
                "tail_05", "tail_06"],
            (208, 1) => [
                "origin", "waist", "head", "jaw", "left_upper_eyelid_a_01", "left_upper_eyelid_a_02",
                "left_upper_eyelid_b_01", "left_upper_eyelid_b_02", "left_upper_eyelid_c_01", "left_upper_eyelid_c_02", "left_upper_eyelid_d_01", "left_upper_eyelid_d_02",
                "left_upper_eyelid_e_01", "left_upper_eyelid_e_02", "left_eyelid_a_01", "left_eyelid_a_02", "left_eyelid_b_01", "left_eyelid_b_02",
                "left_lower_eyelid_b_01", "left_lower_eyelid_b_02", "right_upper_eyelid_a_01", "right_upper_eyelid_a_02", "right_upper_eyelid_b_01", "right_upper_eyelid_b_02",
                "right_upper_eyelid_c_01", "right_upper_eyelid_c_02", "right_upper_eyelid_d_01", "right_upper_eyelid_d_02", "right_upper_eyelid_e_01", "right_upper_eyelid_e_02",
                "right_eyelid_a_01", "right_eyelid_a_02", "right_eyelid_b_01", "right_eyelid_b_02", "right_lower_eyelid_b_01", "right_lower_eyelid_b_02"],
            (323, 1) => [
                "origin", "waist", "neck", "head", "root_jaw", "jaw",
                "lower_jaw", "upper_jaw", "left_upper_eyelid_a", "left_upper_eyelid_b", "left_upper_eyelid_c", "left_upper_eyelid_c_sub",
                "left_upper_eyelid_d", "left_upper_eyelid_d_sub", "left_lower_eyelid_a", "left_lower_eyelid_a_sub", "left_lower_eyelid_b", "left_lower_eyelid_b_sub",
                "left_lower_eyelid_c", "left_lower_eyelid_c_sub", "left_lower_eyelid_d", "left_lower_eyelid_d_sub", "left_lower_eyelid_e", "left_lower_eyelid_e_sub",
                "left_lower_eyelid_f", "left_lower_eyelid_f_sub", "left_lower_eyelid_g", "left_lower_eyelid_g_sub", "left_lower_eyelid_h", "left_lower_eyelid_h_sub",
                "left_lower_eyelid_i", "left_lower_eyelid_i_sub", "left_lower_eyelid_j", "left_lower_eyelid_j_sub", "left_lower_eyelid_k", "right_upper_eyelid_a",
                "right_upper_eyelid_b", "right_upper_eyelid_c", "right_upper_eyelid_c_sub", "right_upper_eyelid_d", "right_upper_eyelid_d_sub", "right_lower_eyelid_a",
                "right_lower_eyelid_a_sub", "right_lower_eyelid_b", "right_lower_eyelid_b_sub", "right_lower_eyelid_c", "right_lower_eyelid_c_sub", "right_lower_eyelid_d",
                "right_lower_eyelid_d_sub", "right_lower_eyelid_e", "right_lower_eyelid_e_sub", "right_lower_eyelid_f", "right_lower_eyelid_f_sub", "right_lower_eyelid_g",
                "right_lower_eyelid_g_sub", "right_lower_eyelid_h", "right_lower_eyelid_h_sub", "right_lower_eyelid_i", "right_lower_eyelid_i_sub", "right_lower_eyelid_j",
                "right_lower_eyelid_j_sub", "right_lower_eyelid_k", "left_hand", "right_hand", "left_foot", "right_foot",
                "tail_01", "tail_02"],
            (358, 1) => [
                "origin", "waist", "spine", "head", "tail_01", "tail_02",
                "tail_03", "tail_04", "tail_05", "tail_06", "upper_jaw", "left_upper_jaw_a",
                "right_upper_jaw_a", "left_upper_jaw_b", "right_upper_jaw_b", "left_upper_jaw_c", "right_upper_jaw_c", "left_lower_jaw_a",
                "right_lower_jaw_a", "left_lower_jaw_b", "right_lower_jaw_b", "left_upper_eyelid_a", "left_upper_eyelid_b", "left_upper_eyelid_g",
                "left_upper_eyelid_h", "right_upper_eyelid_a", "right_upper_eyelid_b", "right_upper_eyelid_g", "right_upper_eyelid_h", "left_shoulder",
                "left_hand", "right_shoulder", "right_hand", "feeler_01", "feeler_02"],
            (384, 1) => [
                "origin", "waist", "spine_01", "spine_02", "neck_01", "neck_02",
                "head", "jaw", "left_upper_eyelid_01", "left_upper_eyelid_02", "right_upper_eyelid_01", "right_upper_eyelid_02",
                "left_arm_01", "left_arm_02", "left_hand", "right_arm_01", "right_arm_02", "right_hand",
                "hips", "tail_01", "tail_02", "tail_03", "tail_04", "tail_05",
                "tail_06", "tail_07", "tail_08", "tail_09", "tail_10", "tail_11"],
            (460, 1) => [
                "origin", "waist", "spine", "head", "jaw_a", "jaw_b",
                "left_feeler_a", "right_feeler_a", "left_feeler_b", "right_feeler_b", "left_eyebrow", "right_eyebrow",
                "left_upper_eyelid_a", "right_upper_eyelid_a", "left_upper_eyelid_b_01", "left_upper_eyelid_b_02", "right_upper_eyelid_b_01", "right_upper_eyelid_b_02",
                "left_upper_eyelid_c_01", "left_upper_eyelid_c_02", "right_upper_eyelid_c_01", "right_upper_eyelid_c_02", "left_lower_eyelid", "right_lower_eyelid",
                "left_arm_01", "left_arm_02", "left_hand", "left_finger_a", "left_finger_b", "left_finger_c",
                "right_arm_01", "right_arm_02", "right_hand", "right_finger_a", "right_finger_b", "right_finger_c",
                "left_feeler_d", "right_feeler_d", "left_feeler_e", "right_feeler_e", "hips", "tail_01",
                "tail_02", "tail_03", "left_leg_01", "left_leg_02", "left_foot", "left_foot_middle",
                "left_foot_ring", "left_foot_index", "right_leg_01", "right_leg_02", "right_foot", "right_foot_middle",
                "right_foot_ring", "right_foot_index"],
            (485, 1) => [
                "origin", "waist", "spine_01", "spine_02", "head", "left_arm_01",
                "left_arm_02", "left_hand", "right_arm_01", "right_arm_02", "right_hand", "hips"],
            (491, 1) => [
                "origin", "waist", "spine", "head", "hair_01", "hair_02",
                "hair_03", "hair_04", "hair_05", "hair_06", "upper_eyelid_a", "upper_eyelid_c",
                "left_arm_01", "left_arm_02", "right_arm_01", "right_arm_02", "hips", "tail_01",
                "tail_02"],
            (500, 1) => [
                "origin", "waist", "spine_01", "spine_02", "head", "jaw",
                "left_upper_eyelid_a", "right_upper_eyelid_a", "left_upper_eyelid_b_01", "left_upper_eyelid_b_02", "right_upper_eyelid_b_01", "right_upper_eyelid_b_02",
                "left_lower_eyelid_c_01", "left_lower_eyelid_c_02", "right_lower_eyelid_c_01", "right_lower_eyelid_c_02", "left_lower_eyelid_d_01", "left_lower_eyelid_d_02",
                "right_lower_eyelid_d_01", "right_lower_eyelid_d_02", "left_shoulder", "left_arm_01", "left_arm_02", "left_hand",
                "right_shoulder", "right_arm_01", "right_arm_02", "right_hand", "hips", "left_leg_01",
                "left_leg_02", "left_foot", "left_foot_index", "right_leg_01", "right_leg_02", "right_foot",
                "right_foot_index"],
            (530, 1) => [
                "origin", "waist", "spine", "neck", "head", "nose",
                "jaw", "left_upper_eyelid_b", "right_upper_eyelid_b", "left_arm_02", "left_hand", "right_arm_02",
                "right_hand", "hips", "left_leg_01", "left_leg_02", "left_foot", "left_foot_index",
                "right_leg_01", "right_leg_02", "right_foot", "right_foot_index"],
            (604, 1) => [
                "origin", "waist", "spine_01", "spine_02", "neck", "head",
                "left_upper_teeth", "right_upper_teeth", "left_lower_teeth", "right_lower_teeth", "left_hand", "right_hand",
                "hips", "tail_01", "tail_02", "tail_03", "tail_04", "tail_05",
                "left_leg_01", "left_leg_02", "left_foot", "right_leg_01", "right_leg_02"],
            (609, 1) => [
                "origin", "waist", "feeler_a_01", "feeler_a_02", "feeler_a_03", "feeler_a_04",
                "feeler_a_05", "feeler_a_06", "feeler_a_07", "feeler_a_08", "feeler_a_09", "feeler_a_10",
                "feeler_a_11", "left_upper_eyelid_a", "right_upper_eyelid_a", "left_upper_eyelid_b", "right_upper_eyelid_b", "left_upper_eyelid_d",
                "right_upper_eyelid_d", "left_lower_eyelid_c", "right_lower_eyelid_c", "left_hand_a_01", "left_hand_a_02", "left_hand_a_03",
                "right_hand_a_01", "right_hand_a_02", "right_hand_a_03", "left_hand_b_01", "left_hand_b_02", "left_hand_b_03",
                "right_hand_b_01", "right_hand_b_02", "right_hand_b_03", "feeler_b"],
            (655, 1) => [
                "origin", "waist", "spine_01", "spine_02", "neck", "head",
                "left_hair_a", "left_hair_c_01", "left_hair_c_02", "right_hair_a", "right_hair_c_01", "right_hair_c_02",
                "nose", "left_feeler_a_01", "left_feeler_a_02", "right_feeler_a_01", "right_feeler_a_02", "left_upper_eyelid_a",
                "right_upper_eyelid_a", "left_upper_eyelid_b", "right_upper_eyelid_b", "left_lower_eyelid_a", "right_lower_eyelid_a", "left_lower_eyelid_b",
                "right_lower_eyelid_b", "left_lower_eyelid_c", "right_lower_eyelid_c", "jaw", "left_shoulder", "left_arm_01",
                "left_arm_02", "left_hand", "left_index_01", "left_index_02", "left_middle_01", "left_middle_02",
                "left_ring_01", "left_ring_02", "right_shoulder", "right_arm_01", "right_arm_02", "right_hand",
                "right_index_01", "right_index_02", "right_middle_01", "right_middle_02", "right_ring_01", "right_ring_02",
                "hips", "left_leg_01", "left_leg_02", "left_foot", "left_foot_index", "right_leg_01",
                "right_leg_02", "right_foot", "right_foot_index", "feeler_a"],
            (687, 1) => [
                "origin", "waist", "head", "left_arm_01", "left_arm_02", "left_arm_03",
                "left_arm_04", "left_arm_05", "left_arm_06", "left_arm_07", "right_arm_01", "right_arm_02",
                "right_arm_03", "right_arm_04", "right_arm_05", "right_arm_06", "right_arm_07", "hips"],
            (689, 1) => [
                "origin", "waist", "spine_01", "spine_02", "left_upper_eyelid_a_01", "left_upper_eyelid_a_02",
                "right_upper_eyelid_a_01", "right_upper_eyelid_a_02", "left_lower_eyelid_b", "right_lower_eyelid_b", "left_lower_eyelid_d", "right_lower_eyelid_d",
                "hips", "left_leg_01", "left_leg_02", "left_foot", "left_foot_index", "right_leg_01",
                "right_leg_02", "right_foot", "right_foot_index", "left_arm_a_01", "left_arm_a_02", "left_hand_a",
                "left_index_a", "left_middle_a", "right_arm_a_01", "right_arm_a_02", "right_hand_a", "right_index_a",
                "right_middle_a", "left_arm_b_01", "left_arm_b_02", "left_hand_b", "left_index_b", "left_middle_b",
                "right_arm_b_01", "right_arm_b_02", "right_hand_b", "right_index_b", "right_middle_b"],
            (718, 5) => [
                "origin", "waist", "spine_01", "spine_02", "head", "left_hand",
                "left_index", "left_middle", "left_ring", "right_hand", "right_index", "right_middle",
                "right_ring", "tail_01", "tail_02", "tail_03", "tail_04", "tail_05",
                "tail_06", "tail_07", "tail_08", "left_feeler_a", "right_feeler_a", "left_feeler_b",
                "right_feeler_b", "left_feeler_c", "right_feeler_c", "hips", "left_crotch", "left_leg_01",
                "left_leg_02", "left_foot", "left_foot_index", "right_crotch", "right_leg_01", "right_leg_02",
                "right_foot", "right_foot_index"],
            (719, 1) => [
                "origin", "waist", "head", "upper_jaw", "left_upper_eyelid_b_01", "left_upper_eyelid_b_02",
                "left_upper_eyelid_c_01", "left_upper_eyelid_c_02", "left_lower_eyelid_c_01", "left_lower_eyelid_c_02", "right_upper_eyelid_b_01", "right_upper_eyelid_b_02",
                "right_upper_eyelid_c_01", "right_upper_eyelid_c_02", "right_lower_eyelid_c_01", "right_lower_eyelid_c_02", "left_arm_01", "left_arm_02",
                "left_hand", "right_arm_01", "right_arm_02", "right_hand", "hips"],
            (801, 2) => [
                "origin", "waist", "spine", "head", "left_arm_01", "left_arm_02",
                "left_hand", "right_arm_01", "right_arm_02", "right_hand", "hips", "left_crotch",
                "left_leg_01", "left_leg_02", "left_foot", "right_crotch", "right_leg_01", "right_leg_02",
                "right_foot", "skirt"],
            (801, 3) => [
                "origin", "waist", "spine", "head", "left_arm_01", "left_arm_02",
                "left_hand", "right_arm_01", "right_arm_02", "right_hand", "hips", "left_crotch",
                "left_leg_01", "left_leg_02", "left_foot", "right_crotch", "right_leg_01", "right_leg_02",
                "right_foot", "skirt"],
            (807, 1) => [
                "origin", "waist", "spine_01", "spine_02", "neck", "head",
                "jaw", "left_ear", "right_ear", "left_feeler_a", "right_feeler_a", "hair_b",
                "left_upper_eyelid_b", "right_upper_eyelid_b", "left_lower_eyelid_a", "right_lower_eyelid_a", "left_lower_eyelid_b", "right_lower_eyelid_b",
                "left_shoulder", "left_arm_01", "left_arm_02", "left_hand", "left_arm_02_roll", "left_hand_sub",
                "left_index_01", "left_index_02", "left_index_03", "left_middle_01", "left_middle_02", "left_middle_03",
                "left_ring_01", "left_ring_02", "left_ring_03", "left_pinky_01", "left_pinky_02", "left_pinky_03",
                "right_shoulder", "right_arm_01", "right_arm_02", "right_hand", "right_arm_02_roll", "right_hand_sub",
                "right_index_01", "right_index_02", "right_index_03", "right_middle_01", "right_middle_02", "right_middle_03",
                "right_ring_01", "right_ring_02", "right_ring_03", "right_pinky_01", "right_pinky_02", "right_pinky_03",
                "left_feeler_b", "right_feeler_b", "left_hair_d", "right_hair_d", "hips", "left_leg_01",
                "left_leg_02", "left_foot", "left_toe", "right_leg_01", "right_leg_02", "right_foot",
                "right_toe"],
            _ => null
        };

        private static IReadOnlyDictionary<string, string>? ExtendedMegaTranslationReferences(string stem, int species, int form)
        {
            var qualifiedStem = (species, form) switch
            {
                (358, 1) => "pm0358_51_00",
                (460, 1) => "pm0460_51_00",
                (718, 5) => "pm0770_51_00",
                (801, 2) => "pm0882_51_00",
                (801, 3) => "pm0882_52_00",
                (807, 1) => "pm0888_51_00",
                _ => null
            };
            if (qualifiedStem is null) return null;
            if (stem != qualifiedStem) throw new InvalidDataException("Mega motion references require the qualified catalog donor.");
            return (species, form) switch
            {
                (358, 1) => new Dictionary<string, string>(StringComparer.Ordinal) { ["upper_jaw"] = "head", ["left_upper_jaw_a"] = "head", ["right_upper_jaw_a"] = "head", ["left_upper_jaw_b"] = "head", ["right_upper_jaw_b"] = "head", ["left_upper_jaw_c"] = "head", ["right_upper_jaw_c"] = "head", ["left_lower_jaw_a"] = "head", ["right_lower_jaw_a"] = "head", ["left_lower_jaw_b"] = "head", ["right_lower_jaw_b"] = "head" },
                (460, 1) => new Dictionary<string, string>(StringComparer.Ordinal) { ["jaw_b"] = "jaw_a" },
                (718, 5) => new Dictionary<string, string>(StringComparer.Ordinal) { ["spine_01"] = "spine_02", ["hips"] = "left_leg_01" },
                (801, 2) => new Dictionary<string, string>(StringComparer.Ordinal) { ["spine"] = "head", ["hips"] = "left_leg_01" },
                (801, 3) => new Dictionary<string, string>(StringComparer.Ordinal) { ["spine"] = "head", ["hips"] = "left_leg_01" },
                (807, 1) => new Dictionary<string, string>(StringComparer.Ordinal) { ["spine_01"] = "spine_02", ["hips"] = "left_leg_01", ["left_arm_02_roll"] = "left_arm_02", ["right_arm_02_roll"] = "right_arm_02" },
                _ => null
            };
        }
    }
}
