package com.rightpad.capture;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.StandardCopyOption;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Properties;

/** Versioned normalized geometry, keyed by stable control ID. Atomic disk publication. */
final class ScreenControlLayoutStore {
    private final File file;
    ScreenControlLayoutStore(File file) { this.file = file; }

    Map<String, ControlRect> load(List<ScreenControlDefinition> definitions, int width, int height)
            throws IOException {
        Properties data = new Properties();
        if (file.exists()) {
            try (FileInputStream input = new FileInputStream(file)) { data.load(input); }
            catch (IllegalArgumentException malformed) { data.clear(); }
        }
        Map<String, ControlRect> result = new LinkedHashMap<>();
        for (ScreenControlDefinition definition : definitions) {
            ControlRect rect = definition.defaultRect(width, height);
            if ("1".equals(data.getProperty("version"))) {
                String key = "controls." + definition.id + ".";
                try {
                    rect = new ControlRect(pixel(data, key + "xRatio", width),
                            pixel(data, key + "yRatio", height),
                            pixel(data, key + "widthRatio", width),
                            pixel(data, key + "heightRatio", height)).clamp(width, height);
                } catch (IllegalArgumentException malformed) { /* Definition default. */ }
            }
            result.put(definition.id, rect);
        }
        return result;
    }
    private static int pixel(Properties data, String key, int extent) {
        double ratio = Double.parseDouble(data.getProperty(key, "NaN"));
        if (!Double.isFinite(ratio)) throw new IllegalArgumentException("Non-finite geometry");
        return (int) Math.round(Math.max(0, Math.min(1, ratio)) * extent);
    }
    void save(Map<String, ControlRect> controls, int width, int height) throws IOException {
        Properties data = new Properties();
        data.setProperty("version", "1");
        for (Map.Entry<String, ControlRect> entry : controls.entrySet()) {
            ControlRect r = entry.getValue();
            if (!r.valid(width, height)) throw new IllegalArgumentException("Control outside View bounds");
            String key = "controls." + entry.getKey() + ".";
            data.setProperty(key + "xRatio", Double.toString(r.x / (double) width));
            data.setProperty(key + "yRatio", Double.toString(r.y / (double) height));
            data.setProperty(key + "widthRatio", Double.toString(r.width / (double) width));
            data.setProperty(key + "heightRatio", Double.toString(r.height / (double) height));
        }
        File temporary = new File(file.getPath() + ".tmp");
        try (FileOutputStream output = new FileOutputStream(temporary)) {
            data.store(output, "Rightpad screen control layout");
            output.getFD().sync();
        }
        Files.move(temporary.toPath(), file.toPath(), StandardCopyOption.REPLACE_EXISTING,
                StandardCopyOption.ATOMIC_MOVE);
    }
}
