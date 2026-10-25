package pt.yaku.booru;

import lombok.Getter;

@Getter
public enum PoolOrder {
    UPDATED("updated_at", "pools.order.updated"),
    NAME("name", "pools.order.name"),
    CREATED("created_at", "pools.order.created"),
    SIZE("post_count", "pools.order.size");

    private final String apiValue;
    private final String labelKey;

    PoolOrder(String apiValue, String labelKey) {
        this.apiValue = apiValue;
        this.labelKey = labelKey;
    }
}
