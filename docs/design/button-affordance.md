# Action-button visibility

The owner's [#1721](https://github.com/diegoami/imperial_conquest_2/issues/1721)
requests clearer clickable buttons on Parchment, suggesting elevation. The private
original manual chapter 9 screenshot `M07_15` shows raised OK/Cancel controls;
it is a reference, not a file to redistribute. The remake's colour palette and
the treatment below are designed UI choices, not an original-game parity claim.

Ordinary action buttons use a contrasting outline, raised fill and lower edge
before hovering. Hover changes the fill/outline; pressing reverses the edge and
removes the shadow. Keyboard focus has a separate two-pixel outline. Disabled
actions remain flat on the panel surface. Explicit content margins and existing
font sizes are retained. Separate style resources leave dropdowns, menu buttons,
checkboxes and static panels unchanged; semantic palette roles keep live theme
switching consistent across Parchment, Dark, High contrast and Slate blue.

`ButtonAffordanceCheck` exercises real controls and checks text contrast of at
least 4.5:1, idle outlines of at least 3:1 against fill and panel, distinct
pressed/disabled geometry, keyboard focus and a callback. Reverting the theme
to its prior styles makes the check fail. Run headlessly like the other checks;
set `IC2_BUTTON_REVIEW_DIR` for optional windowed captures of this UI-only scene.
Automated contrast does not replace the owner's visual assessment.

Authored engine-rendered examples, with available, disabled, selected and
keyboard-focused controls from top to bottom:

- [Parchment](../images/v09x-buttons-parchment.png)
- [Dark](../images/v09x-buttons-dark.png)
- [High contrast](../images/v09x-buttons-contrast.png)
- [Slate blue](../images/v09x-buttons-slate.png)

These are presentation changes. No campaign state, command legality, settings
schema or save bytes change.
