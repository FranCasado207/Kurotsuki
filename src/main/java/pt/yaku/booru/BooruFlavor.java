package pt.yaku.booru;

import lombok.Getter;

@Getter
public enum BooruFlavor {
    MOEBOORU("Moebooru"),
    SHIMMIE("Shimmie");

    private final String label;

    BooruFlavor(String label) {
        this.label = label;
    }
}
