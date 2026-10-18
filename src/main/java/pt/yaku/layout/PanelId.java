package pt.yaku.layout;

public enum PanelId {
    SEARCH("layout.panel.search", Dock.LEFT),
    FAVORITE_TAGS("layout.panel.favoriteTags", Dock.LEFT),
    DOWNLOADS("layout.panel.downloads", Dock.BOTTOM);

    private final String labelKey;
    private final Dock defaultDock;

    PanelId(String labelKey, Dock defaultDock) {
        this.labelKey = labelKey;
        this.defaultDock = defaultDock;
    }

    public String labelKey() {
        return labelKey;
    }

    public Dock defaultDock() {
        return defaultDock;
    }
}
