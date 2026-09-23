/* SPDX-License-Identifier: GPL-3.0-only */

// Exact compatibility patterns for older UI that assembles its display text.
// Captured names and values remain data; only explicitly named UI terms translate.
type EditorLiteralPattern = {
  pattern: RegExp;
  template: string;
  parameters: readonly string[];
  translate?: readonly string[];
};

export const editorLiteralPatterns: readonly EditorLiteralPattern[] = [
  { pattern: /^Output root creation requires Base RomFS and Base ExeFS to validate for (.+)\.$/u, template: 'Validate Base RomFS and Base ExeFS for {game} before creating the output root.', parameters: ['game'] },
  { pattern: /^(.+), slot (\d+) - (.+)$/u, template: '{name}, slot {slot} - {field}', parameters: ['name', 'slot', 'field'], translate: ['field'] },
  { pattern: /^(.+) \(pending changes\)$/u, template: '{name} (pending changes)', parameters: ['name'] },
  { pattern: /^The (.+) status request stopped before completion\. Retry the check\. Editors can still read the configured project sources\.$/u, template: 'The {cache} status request stopped. Retry the check. Editors can still read the configured sources.', parameters: ['cache'], translate: ['cache'] },
  { pattern: /^The (.+) build stopped because the next batch could not be verified\. Existing cache data and the last measured progress remain available\. Retry to continue from the last verified item\.$/u, template: 'The {cache} build paused because the next batch could not be verified. Existing data and measured progress are retained. Retry from the last verified item.', parameters: ['cache'], translate: ['cache'] },
  { pattern: /^Complete the required (.+) source path in Project Setup\.$/u, template: 'Complete the {cache} source path in Project Setup.', parameters: ['cache'], translate: ['cache'] },
  { pattern: /^Reading the current (.+) status\.\.\.$/u, template: 'Reading {cache} status...', parameters: ['cache'], translate: ['cache'] },
  { pattern: /^Persistent (.+) warmup is off in Minimal mode\.$/u, template: 'Persistent {cache} warmup is off in Minimal mode.', parameters: ['cache'], translate: ['cache'] },
  { pattern: /^(.+ cache) setup required$/iu, template: '{cache} setup required', parameters: ['cache'], translate: ['cache'] },
  { pattern: /^(.+) \(last known\)$/u, template: '{value} (last known)', parameters: ['value'], translate: ['value'] },
  { pattern: /^(\d+)% \((\d+) of (\d+), last known\)$/u, template: '{percent}% ({completed} of {total}, last known)', parameters: ['percent', 'completed', 'total'] },
  { pattern: /^(\d+) (evolution|learnset) draft row\(s\) contain invalid values\.$/u, template: 'Invalid {kind} draft rows: {count}.', parameters: ['count', 'kind'], translate: ['kind'] },
  { pattern: /^(\d+) compatibility draft value\(s\) are invalid\.$/u, template: 'Invalid compatibility draft values: {count}.', parameters: ['count'] },
  { pattern: /^(\d+) fields? need valid values$/u, template: 'Fields needing valid values: {count}', parameters: ['count'] },
  { pattern: /^(\d+) Trainer drafts? need valid values before staging\.$/u, template: 'Trainer drafts needing valid values before staging: {count}.', parameters: ['count'] },
  { pattern: /^(.+) projected stat$/u, template: '{stat} projected stat', parameters: ['stat'], translate: ['stat'] },
  { pattern: /^(\d+) answer outcomes?$/u, template: 'Answer outcomes: {count}', parameters: ['count'] },
  { pattern: /^(Learnset|Evolution) slot #(\d+) (.+)$/u, template: '{kind} slot #{slot}: {action}', parameters: ['kind', 'slot', 'action'], translate: ['kind', 'action'] },
  { pattern: /^Move to slot #(\d+)$/u, template: 'Move to slot #{slot}', parameters: ['slot'] },
  { pattern: /^(\d+) shop (item|price|field) draft\(s\) contain invalid values\.$/u, template: 'Invalid shop {kind} drafts: {count}.', parameters: ['count', 'kind'], translate: ['kind'] },
  { pattern: /^(\d+) global item stack-cap draft\(s\) contain invalid values\.$/u, template: 'Invalid item stack limit drafts: {count}.', parameters: ['count'] },
  { pattern: /^Total lot weight: (\d+)$/u, template: 'Total lot weight: {weight}', parameters: ['weight'] },
  { pattern: /^Total chance: ([\d.]+)%$/u, template: 'Total chance: {percent}%', parameters: ['percent'] },
  { pattern: /^(.+) linked spawners$/u, template: '{group} linked spawners', parameters: ['group'] },
  { pattern: /^Apply to (.+)$/u, template: 'Apply to {area}', parameters: ['area'] },
  { pattern: /^Drop chance ([\d./]+)%$/u, template: 'Drop chance {percent}%', parameters: ['percent'] },
  { pattern: /^Placement spawner transforms in (.+)$/u, template: 'Placement spawner transforms in {location}', parameters: ['location'] },
  { pattern: /^(\d+) badge level cap$/u, template: 'Level cap for {count} badges', parameters: ['count'] },
  { pattern: /^Story level cap (\d+)$/u, template: 'Story level cap {slot}', parameters: ['slot'] },
  { pattern: /^Starting item slot (\d+)$/u, template: 'Starting item slot {slot}', parameters: ['slot'] },
  { pattern: /^Starting item quantity (\d+)$/u, template: 'Starting item quantity {slot}', parameters: ['slot'] },
  { pattern: /^Confirm (Restore|Remove) (EXP Yield|EV Yield)$/u, template: 'Confirm {action}: {kind}', parameters: ['action', 'kind'], translate: ['action', 'kind'] },
  { pattern: /^KM Editor v([\d.]+) is available\. Install it now\?$/u, template: 'KM Editor v{version} is available. Install it now?', parameters: ['version'] },
  { pattern: /^KM Editor v([\d.]+) is available\. KM Editor will open (.+)\.$/u, template: 'KM Editor v{version} is available. KM Editor will open {release}.', parameters: ['version', 'release'] },
  { pattern: /^Mastery Lv\. (\d+)$/u, template: 'Mastery level {level}', parameters: ['level'] },
  { pattern: /^(Lv\. 0 \(Evolution\)|Relearn|Lv\. \d+) \/ (Mastery Lv\. \d+)$/u, template: '{condition} / {mastery}', parameters: ['condition', 'mastery'], translate: ['condition', 'mastery'] },
  { pattern: /^((?:Lv\. 0 \(Evolution\)|Relearn|Lv\. \d+)(?: \/ Mastery Lv\. \d+)?) (.+)$/u, template: '{condition} {move}', parameters: ['condition', 'move'], translate: ['condition'] },
  { pattern: /^Maximum length: (\d+) UTF-8 bytes\.$/u, template: 'Maximum length: {count} UTF-8 bytes.', parameters: ['count'] },
  { pattern: /^(\d+) guaranteed perfect$/u, template: 'Guaranteed perfect IVs: {count}', parameters: ['count'] },
  { pattern: /^Dimension Dungeon ([\d,.]+)$/u, template: 'Dimension Dungeon {index}', parameters: ['index'] },
  { pattern: /^Also used by (.+)$/u, template: 'Also used by {names}', parameters: ['names'] },
  { pattern: /^Closest input is 1\/([\d,.\s]+)\. $/u, template: 'Closest input is 1/{denominator}. ', parameters: ['denominator'] },
  { pattern: /^(.+) minimum cannot be greater than its maximum\.$/u, template: '{field} minimum cannot be greater than its maximum.', parameters: ['field'], translate: ['field'] },
  { pattern: /^Stat-change slot (\d+) must be completely empty or include a stat and stage change\.$/u, template: 'Stat change slot {slot} must be empty or include both a stat and a stage change.', parameters: ['slot'] },
  { pattern: /^Projectile replacement (\d+) must specify both projectiles or leave both as None\.$/u, template: 'Projectile replacement {slot} requires both projectiles or neither.', parameters: ['slot'] },
  { pattern: /^Projectile replacement (\d+) cannot follow an empty replacement slot\.$/u, template: 'Projectile replacement {slot} cannot follow an empty slot.', parameters: ['slot'] },
  { pattern: /^Only one (.+) is available for the current S\/V location filter\.$/u, template: 'Only one {kind} is available for the current S/V location filter.', parameters: ['kind'], translate: ['kind'] },
  { pattern: /^Choose a (.+) available inside the current S\/V location filter\.$/u, template: 'Choose a {kind} within the current S/V location filter.', parameters: ['kind'], translate: ['kind'] }
];
