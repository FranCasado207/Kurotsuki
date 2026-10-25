package pt.yaku.ui;

import java.util.ArrayList;
import java.util.List;

import javafx.geometry.Insets;
import javafx.scene.Node;
import javafx.scene.control.Label;
import javafx.scene.layout.FlowPane;
import javafx.scene.layout.HBox;
import javafx.scene.layout.VBox;
import javafx.util.Duration;
import javafx.animation.PauseTransition;
import javafx.geometry.Bounds;
import javafx.geometry.Rectangle2D;
import javafx.scene.Group;
import javafx.scene.Scene;
import javafx.scene.input.MouseEvent;
import javafx.scene.input.ScrollEvent;
import javafx.stage.Popup;
import javafx.stage.Screen;
import pt.yaku.booru.Post;
import pt.yaku.booru.TagCategory;
import pt.yaku.i18n.Messages;

/**
 * The card shown when hovering a thumbnail: id, rating, score and size on top, then only the tags that identify
 * the post (artist, series, characters) and a few general ones, with a count for the rest.
 */
final class PostHoverCard {

    private static final double GAP = 8;
    private static final int MAX_ARTISTS = 2;
    private static final int MAX_COPYRIGHTS = 2;
    private static final int MAX_CHARACTERS = 4;
    private static final int MAX_GENERAL = 6;

    private PostHoverCard() {
    }

    /**
     * Shows the card in its own popup beside the tile after a short delay. A real Tooltip is placed next to the
     * pointer and, once it grew past a few lines, ended up under it, which counted as leaving the tile and hid it
     * again. This popup ignores the mouse and sits beside the tile instead.
     */
    static void install(Node node, Post post) {
        Popup popup = new Popup();
        popup.setAutoFix(false);
        popup.setAutoHide(false);
        popup.setConsumeAutoHidingEvents(false);
        PauseTransition delay = new PauseTransition(Duration.millis(400));
        delay.setOnFinished(event -> show(popup, node, post));
        node.addEventHandler(MouseEvent.MOUSE_ENTERED, event -> {
            if (!event.isPrimaryButtonDown()) {
                delay.playFromStart();
            }
        });
        for (var type : List.of(MouseEvent.MOUSE_EXITED, MouseEvent.MOUSE_PRESSED, MouseEvent.MOUSE_DRAGGED)) {
            node.addEventHandler(type, event -> {
                delay.stop();
                popup.hide();
            });
        }
        node.addEventHandler(ScrollEvent.ANY, event -> {
            delay.stop();
            popup.hide();
        });
    }

    private static void show(Popup popup, Node node, Post post) {
        if (node.getScene() == null || node.getScene().getWindow() == null || !node.isHover()) {
            return;
        }
        Node card = build(post);
        card.setMouseTransparent(true);
        popup.getContent().setAll(card);
        // Size it now so the side can be chosen before it is shown.
        Group measure = new Group(card);
        Scene scene = new Scene(measure);
        scene.getStylesheets().setAll(node.getScene().getStylesheets());
        measure.applyCss();
        measure.layout();
        double width = card.prefWidth(-1);
        double height = card.prefHeight(width);
        measure.getChildren().clear();
        popup.getContent().setAll(card);

        Bounds bounds = node.localToScreen(node.getBoundsInLocal());
        if (bounds == null) {
            return;
        }
        Rectangle2D screen = Screen.getScreensForRectangle(bounds.getMinX(), bounds.getMinY(), 1, 1).stream()
                .findFirst().orElse(Screen.getPrimary()).getVisualBounds();
        double x = bounds.getMaxX() + GAP;
        if (x + width > screen.getMaxX()) {
            x = bounds.getMinX() - GAP - width;
        }
        double y = Math.min(bounds.getMinY(), screen.getMaxY() - height - GAP);
        popup.show(node, Math.max(screen.getMinX(), x), Math.max(screen.getMinY(), y));
    }

    static Node build(Post post) {
        VBox card = new VBox(6);
        card.getStyleClass().add("hover-card");
        card.setPadding(new Insets(8, 10, 8, 10));
        card.setMaxWidth(300);

        Label title = new Label("#" + post.getId());
        title.getStyleClass().add("hover-title");
        HBox header = new HBox(8, title);
        Label rating = new Label(ratingLabel(post.getRating()));
        rating.getStyleClass().addAll("hover-rating", "hover-rating-" + ratingClass(post.getRating()));
        header.getChildren().add(rating);
        card.getChildren().add(header);

        StringBuilder meta = new StringBuilder();
        if (post.getWidth() > 0 && post.getHeight() > 0) {
            meta.append(post.getWidth()).append(" × ").append(post.getHeight());
        }
        if (meta.length() > 0) {
            meta.append("  ·  ");
        }
        meta.append(Messages.get("hover.score", String.valueOf(post.getScore())));
        Label metaLabel = new Label(meta.toString());
        metaLabel.getStyleClass().add("hover-meta");
        card.getChildren().add(metaLabel);

        List<String> tags = tagsOf(post);
        int shown = 0;
        FlowPane chips = new FlowPane(4, 4);
        chips.setPrefWrapLength(280);
        shown += addChips(chips, post, tags, TagCategory.ARTIST, MAX_ARTISTS);
        shown += addChips(chips, post, tags, TagCategory.COPYRIGHT, MAX_COPYRIGHTS);
        shown += addChips(chips, post, tags, TagCategory.CHARACTER, MAX_CHARACTERS);
        shown += addChips(chips, post, tags, TagCategory.GENERAL, MAX_GENERAL);
        if (!chips.getChildren().isEmpty()) {
            card.getChildren().add(chips);
        }
        int more = tags.size() - shown;
        if (more > 0) {
            Label moreLabel = new Label(Messages.get("hover.moreTags", String.valueOf(more)));
            moreLabel.getStyleClass().add("hover-meta");
            card.getChildren().add(moreLabel);
        }
        return card;
    }

    private static int addChips(FlowPane chips, Post post, List<String> tags, TagCategory category, int max) {
        int added = 0;
        for (String tag : tags) {
            if (added == max) {
                break;
            }
            if (post.categoryOf(tag) == category) {
                Label chip = new Label(tag.replace('_', ' '));
                chip.setMaxWidth(280);
                chip.getStyleClass().addAll("tag-chip", "hover-chip", "tag-chip-" + category.name().toLowerCase());
                chips.getChildren().add(chip);
                added++;
            }
        }
        return added;
    }

    static List<String> tagsOf(Post post) {
        List<String> tags = new ArrayList<>();
        if (post.getTags() != null) {
            for (String tag : post.getTags().trim().split("\\s+")) {
                if (!tag.isEmpty()) {
                    tags.add(tag);
                }
            }
        }
        return tags;
    }

    static String ratingClass(String rating) {
        if (rating == null || rating.isEmpty()) {
            return "unknown";
        }
        return switch (Character.toLowerCase(rating.charAt(0))) {
            case 's', 'g' -> "safe";
            case 'q' -> "questionable";
            case 'e' -> "explicit";
            default -> "unknown";
        };
    }

    static String ratingLabel(String rating) {
        return switch (ratingClass(rating)) {
            case "safe" -> Messages.get("hover.ratingSafe");
            case "questionable" -> Messages.get("hover.ratingQuestionable");
            case "explicit" -> Messages.get("hover.ratingExplicit");
            default -> rating == null || rating.isBlank() ? "?" : rating;
        };
    }
}
