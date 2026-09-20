package com.rightpad.capture;

import android.content.Context;
import android.graphics.Color;
import android.text.Editable;
import android.text.InputType;
import android.text.TextWatcher;
import android.view.Gravity;
import android.view.View;
import android.view.inputmethod.InputMethodManager;
import android.widget.Button;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;
import java.io.IOException;

final class ScreenControlEditorPanel extends LinearLayout {
    private final ScreenControls controls;
    private final View surface;
    private final EditText[] fields = new EditText[4];
    private final Button save;
    private final TextView status;
    private boolean syncing;

    ScreenControlEditorPanel(Context context, ScreenControls controls, View surface, Runnable exit) {
        super(context);
        this.controls = controls;
        this.surface = surface;
        setOrientation(VERTICAL);
        setPadding(12, 8, 12, 8);
        setBackgroundColor(0xFF202938);
        setClickable(true); // Panel padding also consumes input.
        setFocusableInTouchMode(true);
        status = new TextView(context);
        status.setTextColor(Color.WHITE);
        addView(status);
        LinearLayout row = new LinearLayout(context);
        String[] names = {"X", "Y", "Width", "Height"};
        for (int i = 0; i < fields.length; i++) {
            LinearLayout column = new LinearLayout(context);
            column.setOrientation(VERTICAL);
            TextView label = new TextView(context);
            label.setText(names[i] + " (px)");
            label.setTextColor(Color.WHITE);
            column.addView(label);
            EditText field = new EditText(context);
            field.setSingleLine(true);
            field.setSelectAllOnFocus(true);
            field.setTextSize(16);
            field.setTextColor(Color.WHITE);
            field.setInputType(InputType.TYPE_CLASS_NUMBER | InputType.TYPE_NUMBER_FLAG_SIGNED);
            field.setContentDescription(names[i] + " (px)");
            fields[i] = field;
            column.addView(field);
            row.addView(column, new LinearLayout.LayoutParams(0, LayoutParams.WRAP_CONTENT, 1));
        }
        addView(row);
        LinearLayout buttons = new LinearLayout(context);
        save = button(buttons, "Save", () -> {
            try {
                controls.layout.save(controls.store);
                exit.run();
            } catch (IOException | IllegalArgumentException error) {
                status.setText("Save failed: " + error.getMessage());
            }
        });
        button(buttons, "Cancel", () -> { controls.layout.cancel(); exit.run(); });
        button(buttons, "Reset", () -> { controls.layout.reset(); sync(); surface.invalidate(); });
        button(buttons, "Panel ↕", () -> {
            FrameLayout.LayoutParams p = (FrameLayout.LayoutParams) getLayoutParams();
            p.gravity = p.gravity == Gravity.BOTTOM ? Gravity.TOP : Gravity.BOTTOM;
            setLayoutParams(p);
            requestApplyInsets();
        });
        addView(buttons);
        for (EditText field : fields) field.addTextChangedListener(new TextWatcher() {
            @Override public void beforeTextChanged(CharSequence s, int start, int count, int after) { }
            @Override public void onTextChanged(CharSequence s, int start, int before, int count) { }
            @Override public void afterTextChanged(Editable value) {
                if (syncing) return;
                boolean valid = controls.layout.numeric(fields[0].getText().toString(), fields[1].getText().toString(),
                        fields[2].getText().toString(), fields[3].getText().toString());
                for (EditText f : fields) f.setError(null);
                if (!valid) field.setError("Integer px; size ≥ 20; rectangle must fit View");
                save.setEnabled(controls.layout.canSave());
                surface.invalidate();
            }
        });
        controls.draftChanged = () -> {
            requestFocus();
            ((InputMethodManager) context.getSystemService(Context.INPUT_METHOD_SERVICE))
                    .hideSoftInputFromWindow(getWindowToken(), 0);
            sync();
        };
        setOnApplyWindowInsetsListener((view, insets) -> {
            if (getLayoutParams() instanceof FrameLayout.LayoutParams) {
                FrameLayout.LayoutParams p = (FrameLayout.LayoutParams) getLayoutParams();
                int bottom = p.gravity == Gravity.BOTTOM
                        ? insets.getInsets(android.view.WindowInsets.Type.ime()).bottom : 0;
                int top = p.gravity == Gravity.TOP
                        ? insets.getInsets(android.view.WindowInsets.Type.displayCutout()).top : 0;
                if (p.bottomMargin != bottom || p.topMargin != top) {
                    p.bottomMargin = bottom;
                    p.topMargin = top;
                    setLayoutParams(p);
                }
            }
            return insets;
        });
        sync();
    }
    @Override protected void onAttachedToWindow() {
        super.onAttachedToWindow();
        requestApplyInsets();
    }
    private Button button(LinearLayout row, String name, Runnable click) {
        Button button = new Button(getContext());
        button.setText(name);
        button.setTextSize(12);
        button.setPadding(0, 0, 0, 0);
        button.setOnClickListener(v -> click.run());
        row.addView(button, new LinearLayout.LayoutParams(0, LayoutParams.WRAP_CONTENT, 1));
        return button;
    }
    private void sync() {
        syncing = true;
        ControlRect r = controls.layout.selectedRect();
        int[] values = {r.x, r.y, r.width, r.height};
        for (int i = 0; i < fields.length; i++) {
            fields[i].setText(Integer.toString(values[i]));
            fields[i].setError(null);
        }
        status.setText("Edit Controls Layout · " + controls.instances.get(controls.layout.selectedId()).definition.label);
        save.setEnabled(controls.layout.canSave());
        syncing = false;
    }
}
