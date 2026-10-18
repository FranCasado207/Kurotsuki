package pt.yaku.layout;

import java.io.IOException;
import java.util.EnumMap;
import java.util.HashMap;
import java.util.Map;
import java.util.Objects;
import java.util.function.Consumer;

import pt.yaku.context.ApplicationContext;

public class PanelLayout {

    private static final double SIDE_SIZE = 280;
    private static final double ROW_SIZE = 200;

    private final ApplicationContext context;
    private final Consumer<String> onError;
    private final Map<PanelId, Dock> docks = new EnumMap<>(PanelId.class);
    private final Map<String, Double> sizes = new HashMap<>();

    public PanelLayout(ApplicationContext context, Consumer<String> onError) {
        this.context = context;
        this.onError = onError;
        for (Map.Entry<String, String> entry : Objects.requireNonNullElse(
                context.getUserConfig().getPanelDocks(), Map.<String, String>of()).entrySet()) {
            try {
                docks.put(PanelId.valueOf(entry.getKey()), Dock.valueOf(entry.getValue()));
            } catch (IllegalArgumentException ignored) {
            }
        }
        sizes.putAll(Objects.requireNonNullElse(context.getUserConfig().getPanelSizes(), Map.of()));
    }

    public Dock dockOf(PanelId id) {
        return docks.getOrDefault(id, id.defaultDock());
    }

    public double sizeOf(Dock dock) {
        return sizes.getOrDefault(dock.name(), defaultSize(dock));
    }

    public Double panelSizeOf(PanelId id) {
        return sizes.get(id.name());
    }

    public void move(PanelId id, Dock dock) {
        docks.put(id, dock);
        persist();
    }

    public void setSize(Dock dock, double size) {
        sizes.put(dock.name(), size);
        persist();
    }

    public void setPanelSize(PanelId id, double size) {
        sizes.put(id.name(), size);
        persist();
    }

    public void reset() {
        docks.clear();
        sizes.clear();
        persist();
    }

    private static double defaultSize(Dock dock) {
        return dock == Dock.TOP || dock == Dock.BOTTOM ? ROW_SIZE : SIDE_SIZE;
    }

    private void persist() {
        Map<String, String> dockData = new HashMap<>();
        docks.forEach((id, dock) -> dockData.put(id.name(), dock.name()));
        context.getUserConfig().setPanelDocks(dockData);

        context.getUserConfig().setPanelSizes(new HashMap<>(sizes));

        try {
            context.saveUserConfig();
        } catch (IOException e) {
            onError.accept(e.getMessage());
        }
    }
}
