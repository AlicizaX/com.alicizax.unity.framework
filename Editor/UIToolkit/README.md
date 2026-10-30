# AlicizaX UI Toolkit Editor Toolkit

A small, skin-independent UI Toolkit toolkit for AlicizaX editor inspectors.

## Requirements

- Unity 2022.3 or newer.
- Editor-only code. The entry points live in `Editor/UIToolkit`.

The current implementation is compiled against Unity `2022.3.62f3` and `6000.5.9f1`.
It uses only stable editor APIs (`Editor.CreateInspectorGUI`, `BaseField<T>`, `BindProperty`,
`StyleSheet`, USS custom properties) and intentionally avoids `UxmlElement`/`UxmlFactory`,
so the same source compiles on Unity 2022.3 and Unity 6.x.

## Quick start

```csharp
using AlicizaX.Editor.UIToolkit;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

[CustomEditor(typeof(MyComponent))]
internal sealed class MyComponentInspector : AlicizaUIEditor
{
    protected override void BuildInspector(VisualElement root)
    {
        AlicizaPanel panel = AlicizaEditorUI.CreatePanel("My Component");
        root.Add(panel);

        var mode = new EnumField(MyMode.Default) { label = string.Empty };
        mode.BindProperty(Find("m_Mode"));
        AlicizaEditorUI.AddRow(panel.Body, "Mode", mode);

        var enabled = new AlicizaPillToggle();
        enabled.BindProperty(Find("m_Enabled"));
        AlicizaEditorUI.AddRow(panel.Body, "Enabled", enabled);

        Foldout advanced = AlicizaEditorUI.CreateSection("Advanced");
        panel.Body.Add(advanced);

        var opacity = new AlicizaSliderField(0f, 1f);
        opacity.BindProperty(Find("m_Opacity"));
        AlicizaEditorUI.AddRow(advanced.contentContainer, "Opacity", opacity);
    }
}
```

`CreateInspectorGUI()` binds the returned tree implicitly. Use `BindProperty` for controls
that derive from `BaseField<T>`; custom controls can update the owning `SerializedObject`
and call `ApplyModifiedProperties()`.

## Controls

| Type | Purpose |
| --- | --- |
| `AlicizaUIEditor` | Base `Editor` that creates and themes the root element. |
| `AlicizaEditorUI` | Panel, row, section, hint, and icon-button factory methods. |
| `AlicizaPanel` | Header + padded body container. |
| `AlicizaRow` | Fixed-width caption and flexible content row. |
| `AlicizaSection` | Non-collapsible section with a static title and hairline. |
| `AlicizaEnumField` | Flat enum dropdown with a dedicated arrow area, bound directly to a SerializedProperty. |
| `AlicizaPillToggle` | Flat `Enabled`/`Disabled` toggle with full `on`/`off`/hover/focus states. |
| `AlicizaSliderField` | Slider + numeric field bound to a `float` property. |
| `AlicizaStringListField` | Compact string array editor with count, header `+ Add`, and Undo through `SerializedObject`. |

## Theming

`AlicizaEditorTheme.Apply(root)` adds the shared `Styles/AlicizaEditor.uss` sheet and the
`alicia-theme--dark` / `alicia-theme--light` class. Colors and metrics are USS custom properties;
override them in a project-level USS file instead of editing the package.

Row captions are 140 px wide, rows use a 34 px minimum height, and the pill is 76 x 20 px.
Panels use an 8 px radius; the list group uses an 8 px radius with hairline item separators.
These values are defined in USS so a project can change them without touching C#.

The dark palette is intentionally close to Unity Pro: `#363636` panel and header, transparent
field rows separated by `#3F3F3F` hairlines, `#2C2C2C` list group, `#2E2E2E` controls, `#D8D8D8`
text, `#CCCCCC` field captions, and the native steel blue `#2C5D87` for selection/pills. That keeps
custom controls visually consistent with the built-in Slider, Enum, and ObjectField.

## Notes

- Do not inherit platform `EditorStyles` textures for interactive controls. All visual states
  must be defined explicitly; that is the reason this toolkit exists.
- Inspector fields use `flex-shrink: 0` by default. When a field shares a row with other controls,
  set `flex-shrink: 1` and `min-width: 0` (the bundled controls already do this).
- `BaseField<T>.label = string.Empty` removes the field label from the hierarchy. Use the row
  caption for the visible label.
- The style sheet is loaded through `PackageInfo.FindForAssembly`, so the toolkit works from an
  embedded package, a local package, or a registry package.
