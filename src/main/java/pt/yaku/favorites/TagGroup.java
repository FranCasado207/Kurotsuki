package pt.yaku.favorites;

import java.util.List;

import javafx.collections.FXCollections;
import javafx.collections.ObservableList;
import lombok.Getter;

public class TagGroup {

    @Getter
    private final String name;
    @Getter
    private final ObservableList<String> tags;

    public TagGroup(String name, List<String> tags) {
        this.name = name;
        this.tags = FXCollections.observableArrayList(tags);
    }

    public boolean contains(String tag) {
        return tags.contains(tag);
    }

    @Override
    public String toString() {
        return name;
    }
}
