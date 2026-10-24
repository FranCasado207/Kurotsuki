package pt.yaku.ui;

import java.util.function.Supplier;

import javafx.scene.control.CheckMenuItem;
import javafx.scene.control.Menu;
import javafx.scene.control.MenuItem;
import javafx.scene.control.SeparatorMenuItem;
import javafx.stage.Window;
import pt.yaku.favorites.TagGroup;
import pt.yaku.favorites.TagGroups;
import pt.yaku.i18n.Messages;

/** The "Add to group" submenu shared by the sidebar and the tag panel. */
final class TagGroupMenu {

    private TagGroupMenu() {
    }

    static Menu create(TagGroups groups, String tag, Supplier<Window> owner) {
        Menu menu = new Menu(Messages.get("tagGroups.addTo"));
        menu.setOnShowing(event -> {
            menu.getItems().clear();
            for (TagGroup group : groups.getGroups()) {
                CheckMenuItem item = new CheckMenuItem(group.getName());
                item.setSelected(group.contains(tag));
                item.setOnAction(e -> {
                    if (group.contains(tag)) {
                        groups.removeTag(group, tag);
                    } else {
                        groups.addTag(group, tag);
                    }
                });
                menu.getItems().add(item);
            }
            if (!groups.getGroups().isEmpty()) {
                menu.getItems().add(new SeparatorMenuItem());
            }
            MenuItem create = new MenuItem(Messages.get("tagGroups.new"));
            create.setOnAction(e -> askNewGroup(groups, owner.get()).ifPresent(group -> groups.addTag(group, tag)));
            menu.getItems().add(create);
        });
        // A submenu with no items would not open, so it needs a placeholder before its first showing.
        menu.getItems().add(new MenuItem());
        return menu;
    }

    static java.util.Optional<TagGroup> askNewGroup(TagGroups groups, Window owner) {
        return Dialogs.askText(owner, Messages.get("tagGroups.newTitle"), Messages.get("tagGroups.name"))
                .map(groups::create);
    }
}
