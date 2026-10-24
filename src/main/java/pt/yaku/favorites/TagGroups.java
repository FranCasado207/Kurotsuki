package pt.yaku.favorites;

import java.sql.SQLException;
import java.util.List;
import java.util.Map;
import java.util.function.Consumer;

import javafx.collections.FXCollections;
import javafx.collections.ListChangeListener;
import javafx.collections.ObservableList;
import lombok.Getter;
import pt.yaku.i18n.Messages;
import pt.yaku.storage.LibraryStore;
import pt.yaku.storage.StoreWrites;

/**
 * Named groups of favorite tags, to keep a long favorites list organised. A tag can be in several groups; a tag
 * that stops being a favorite leaves all of them.
 */
public class TagGroups {

    private final LibraryStore store;
    private final Favorites favorites;
    private final Consumer<String> onError;
    @Getter
    private final ObservableList<TagGroup> groups = FXCollections.observableArrayList();

    public TagGroups(LibraryStore store, Favorites favorites, Consumer<String> onError) {
        this.store = store;
        this.favorites = favorites;
        this.onError = onError;
        try {
            for (Map.Entry<String, List<String>> group : store.tagGroups().entrySet()) {
                groups.add(new TagGroup(group.getKey(), group.getValue()));
            }
        } catch (SQLException e) {
            onError.accept(Messages.get("tagGroups.saveError", e.getMessage()));
        }
        // The database drops the membership through a foreign key; this keeps the in-memory lists in step.
        favorites.getTags().addListener((ListChangeListener<String>) change -> {
            while (change.next()) {
                if (change.wasRemoved()) {
                    for (TagGroup group : groups) {
                        group.getTags().removeAll(change.getRemoved());
                    }
                }
            }
        });
    }

    public boolean exists(String name) {
        return find(name) != null;
    }

    public TagGroup create(String name) {
        String trimmed = name.trim();
        TagGroup existing = find(trimmed);
        if (existing != null) {
            return existing;
        }
        TagGroup group = new TagGroup(trimmed, List.of());
        groups.add(group);
        persist(() -> store.createTagGroup(trimmed), () -> groups.remove(group));
        return group;
    }

    /** Returns false (and changes nothing) if another group already uses {@code newName}. */
    public boolean rename(TagGroup group, String newName) {
        String trimmed = newName.trim();
        int index = groups.indexOf(group);
        if (index < 0 || trimmed.isEmpty() || (find(trimmed) != null && !trimmed.equals(group.getName()))) {
            return false;
        }
        if (trimmed.equals(group.getName())) {
            return true;
        }
        TagGroup renamed = new TagGroup(trimmed, group.getTags());
        groups.set(index, renamed);
        persist(() -> store.renameTagGroup(group.getName(), trimmed), () -> {
            int current = groups.indexOf(renamed);
            if (current >= 0) {
                groups.set(current, group);
            }
        });
        return true;
    }

    public void delete(TagGroup group) {
        int index = groups.indexOf(group);
        groups.remove(group);
        persist(() -> store.deleteTagGroup(group.getName()), () -> {
            if (!groups.contains(group)) {
                groups.add(Math.min(Math.max(index, 0), groups.size()), group);
            }
        });
    }

    /** Adds the tag to the group, making it a favorite first if it is not one yet. */
    public void addTag(TagGroup group, String tag) {
        if (group.contains(tag)) {
            return;
        }
        if (!favorites.isTagFavorite(tag)) {
            favorites.toggleTag(tag);
        }
        group.getTags().add(tag);
        persist(() -> store.addTagToGroup(group.getName(), tag), () -> group.getTags().remove(tag));
    }

    public void removeTag(TagGroup group, String tag) {
        int index = group.getTags().indexOf(tag);
        if (group.getTags().remove(tag)) {
            persist(() -> store.removeTagFromGroup(group.getName(), tag), () -> {
                if (!group.contains(tag)) {
                    group.getTags().add(Math.min(Math.max(index, 0), group.getTags().size()), tag);
                }
            });
        }
    }

    public boolean isGrouped(String tag) {
        return groups.stream().anyMatch(group -> group.contains(tag));
    }

    private TagGroup find(String name) {
        for (TagGroup group : groups) {
            if (group.getName().equals(name)) {
                return group;
            }
        }
        return null;
    }

    private void persist(LibraryStore.SqlTask write, Runnable undo) {
        StoreWrites.persist(store, "tagGroups.saveError", onError, write, undo);
    }
}
