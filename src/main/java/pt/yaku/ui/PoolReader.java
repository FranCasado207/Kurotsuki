package pt.yaku.ui;

import java.io.IOException;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionException;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.logging.Level;
import java.util.logging.Logger;

import javafx.animation.PauseTransition;
import javafx.application.Platform;
import javafx.geometry.Insets;
import javafx.geometry.Pos;
import javafx.scene.Node;
import javafx.scene.Scene;
import javafx.scene.control.Button;
import javafx.scene.control.Label;
import javafx.scene.control.ScrollPane;
import javafx.scene.control.Slider;
import javafx.scene.image.Image;
import javafx.scene.image.ImageView;
import javafx.scene.input.KeyCode;
import javafx.scene.input.KeyEvent;
import javafx.scene.input.MouseEvent;
import javafx.scene.input.ScrollEvent;
import javafx.scene.layout.BorderPane;
import javafx.scene.layout.HBox;
import javafx.scene.layout.Priority;
import javafx.scene.layout.StackPane;
import javafx.scene.layout.VBox;
import javafx.stage.Stage;
import javafx.util.Duration;
import pt.yaku.booru.Pool;
import pt.yaku.booru.Post;
import pt.yaku.cache.CacheManager;
import pt.yaku.context.ApplicationContext;
import pt.yaku.i18n.Messages;
import pt.yaku.library.Library;
import pt.yaku.user.config.UserConfiguration;

/**
 * Comic reader for a pool. Pages are shown one at a time, as two-page spreads, or as one continuous vertical
 * strip; they fit the window or its width and can be zoomed and panned. The reading direction can be flipped
 * for manga, decoded pages are kept within a memory budget, and the last page read is remembered.
 */
public class PoolReader extends Stage {

    enum Mode { PAGED, SCROLL }

    enum Fit { PAGE, WIDTH }

    private static final Logger LOGGER = Logger.getLogger(PoolReader.class.getName());
    private static final Set<String> IMAGE_TYPES = Set.of("png", "jpg", "jpeg", "gif", "bmp");
    private static final double MAX_DECODED_SIDE = 6000;
    private static final int PREFETCH_GROUPS = 2;
    private static final double TAP_ZONE = 0.3;
    private static final double DEFAULT_ASPECT = 1.4;
    private static final double ZOOM_STEP = 1.25;
    private static final double MAX_ZOOM = 5;

    private final ApplicationContext context;
    private final Library library;
    private final Pool pool;
    private final List<Post> pages;
    private final PageBudget budget;
    private final ExecutorService decoder = Executors.newFixedThreadPool(2, runnable -> {
        Thread thread = new Thread(runnable, "reader-decoder");
        thread.setDaemon(true);
        return thread;
    });
    private final Map<Integer, CompletableFuture<Image>> loads = new HashMap<>();
    private final PauseTransition saveDelay = new PauseTransition(Duration.millis(600));

    private Mode mode;
    private Fit fit;
    private boolean rightToLeft;
    private boolean spread;
    private double zoom = 1;
    private int current;
    private List<int[]> groups;
    private int currentGroup;
    private int showToken;
    private boolean syncing;
    private boolean closed;

    private final BorderPane root = new BorderPane();
    private final StackPane viewArea = new StackPane();
    private final Button previousButton = new Button("‹");
    private final Button nextButton = new Button("›");
    private final Slider slider = new Slider();
    private final Label counter = new Label();
    private final Button modeButton = new Button();
    private final Button fitButton = new Button();
    private final Button directionButton = new Button();
    private final Button spreadButton = new Button();
    private final Button zoomButton = new Button();
    private final Button downloadButton = new Button(Messages.get("common.download"));
    private final Button finishedButton = new Button();
    private final Button fullscreenButton = new Button(Messages.get("reader.fullscreen"));

    private final PageSlot[] slots = {new PageSlot(), new PageSlot()};
    private final HBox slotRow = new HBox(slots[0], slots[1]);
    private final StackPane pageBox = new StackPane(slotRow);
    private final ScrollPane pagedScroll = new ScrollPane(pageBox);

    private final VBox strip = new VBox();
    private final ScrollPane stripScroll = new ScrollPane(strip);
    private final List<Holder> holders = new ArrayList<>();

    /**
     * @param hidden number of pool posts left out because of the tag blacklist, shown in the title
     */
    public PoolReader(Stage owner, ApplicationContext context, Library library, Pool pool, List<Post> pages,
                      int startPage, int hidden) {
        this.context = context;
        this.library = library;
        this.pool = pool;
        this.pages = List.copyOf(pages);
        initOwner(owner);

        UserConfiguration config = context.getUserConfig();
        this.mode = parse(Mode.class, config.getReaderMode(), Mode.PAGED);
        this.fit = parse(Fit.class, config.getReaderFit(), Fit.PAGE);
        this.rightToLeft = config.isReaderRightToLeft();
        this.spread = config.isReaderSpread();
        this.budget = new PageBudget(config.effectiveReaderCacheMegabytes() * 1024L * 1024L);
        this.current = Math.max(0, Math.min(startPage, this.pages.size() - 1));
        this.groups = buildGroups();
        this.currentGroup = SpreadLayout.groupOf(groups, current);

        buildPagedView();
        buildStripView();
        buildToolbar();

        viewArea.getStyleClass().add("reader-stage");
        root.setCenter(viewArea);
        root.setBottom(toolbar());
        root.getStyleClass().add("reader-root");

        Scene scene = new Scene(root, 1000, 780);
        Styles.apply(scene);
        scene.addEventFilter(KeyEvent.KEY_PRESSED, this::onKey);
        setScene(scene);

        saveDelay.setOnFinished(event -> saveProgress());
        setOnHidden(event -> {
            closed = true;
            saveProgress();
            decoder.shutdownNow();
            loads.clear();
        });

        slider.setMin(0);
        slider.setMax(Math.max(0, this.pages.size() - 1));
        slider.setBlockIncrement(1);
        slider.setMajorTickUnit(Math.max(1, this.pages.size() / 10.0));
        slider.valueProperty().addListener((obs, was, is) -> {
            if (!syncing) {
                goTo((int) Math.round(is.doubleValue()));
            }
        });
        if (hidden > 0) {
            titleSuffix = " (" + Messages.get("reader.hidden", String.valueOf(hidden)) + ")";
        }

        applyMode();
        show();
    }

    private String titleSuffix = "";

    private static <E extends Enum<E>> E parse(Class<E> type, String value, E fallback) {
        try {
            return value == null ? fallback : Enum.valueOf(type, value);
        } catch (IllegalArgumentException e) {
            return fallback;
        }
    }

    // ---- layout ---------------------------------------------------------------------------------------------

    private void buildPagedView() {
        slotRow.setAlignment(Pos.CENTER);
        pageBox.setAlignment(Pos.CENTER);
        pageBox.minWidthProperty().bind(pagedScroll.widthProperty().subtract(2));
        pageBox.minHeightProperty().bind(pagedScroll.heightProperty().subtract(2));
        pagedScroll.setFitToWidth(true);
        pagedScroll.setFitToHeight(true);
        pagedScroll.setPannable(true); // when zoomed in, dragging moves the page
        pagedScroll.setHbarPolicy(ScrollPane.ScrollBarPolicy.AS_NEEDED);
        pagedScroll.getStyleClass().add("reader-scroll");
        pagedScroll.viewportBoundsProperty().addListener((obs, was, is) -> fitPageImages());
        pagedScroll.addEventFilter(ScrollEvent.SCROLL, this::onZoomScroll);
        pagedScroll.addEventHandler(MouseEvent.MOUSE_CLICKED, event -> {
            if (!event.isStillSincePress() || zoom > 1.001) {
                return;
            }
            double ratio = event.getX() / Math.max(1, pagedScroll.getWidth());
            if (ratio < TAP_ZONE) {
                step(rightToLeft ? 1 : -1);
            } else if (ratio > 1 - TAP_ZONE) {
                step(rightToLeft ? -1 : 1);
            }
        });
    }

    private void buildStripView() {
        strip.setAlignment(Pos.TOP_CENTER);
        stripScroll.setFitToWidth(true);
        stripScroll.setPannable(true);
        stripScroll.setHbarPolicy(ScrollPane.ScrollBarPolicy.AS_NEEDED);
        stripScroll.getStyleClass().add("reader-scroll");
        stripScroll.addEventFilter(ScrollEvent.SCROLL, this::onZoomScroll);
        stripScroll.vvalueProperty().addListener((obs, was, is) -> updateStrip());
        stripScroll.viewportBoundsProperty().addListener((obs, was, is) -> {
            resizeHolders();
            Platform.runLater(this::updateStrip);
        });
        for (int i = 0; i < pages.size(); i++) {
            Holder holder = new Holder(i);
            holders.add(holder);
            strip.getChildren().add(holder);
        }
    }

    private void buildToolbar() {
        previousButton.setOnAction(event -> step(-1));
        nextButton.setOnAction(event -> step(1));
        modeButton.setOnAction(event -> setMode(mode == Mode.PAGED ? Mode.SCROLL : Mode.PAGED));
        fitButton.setOnAction(event -> setFit(fit == Fit.PAGE ? Fit.WIDTH : Fit.PAGE));
        directionButton.setOnAction(event -> setRightToLeft(!rightToLeft));
        spreadButton.setOnAction(event -> setSpread(!spread));
        zoomButton.setOnAction(event -> setZoom(1));
        zoomButton.setTooltip(new javafx.scene.control.Tooltip(Messages.get("reader.zoomHint")));
        downloadButton.setOnAction(event -> library.queue().downloadPool(pool));
        finishedButton.setOnAction(event -> {
            library.reading().setFinished(pool, !library.reading().isFinished(pool.key()));
            updateToolbarLabels();
        });
        fullscreenButton.setOnAction(event -> setFullScreen(!isFullScreen()));
        counter.getStyleClass().add("reader-counter");
        counter.setMinWidth(80);
        counter.setAlignment(Pos.CENTER);
    }

    private Node toolbar() {
        HBox.setHgrow(slider, Priority.ALWAYS);
        HBox navigation = new HBox(10, previousButton, slider, counter, nextButton);
        navigation.setAlignment(Pos.CENTER_LEFT);
        HBox options = new HBox(8, modeButton, spreadButton, fitButton, directionButton, zoomButton,
                downloadButton, finishedButton, fullscreenButton);
        options.setAlignment(Pos.CENTER_LEFT);
        VBox bar = new VBox(6, navigation, options);
        bar.setPadding(new Insets(8, 12, 8, 12));
        bar.getStyleClass().add("reader-toolbar");
        return bar;
    }

    // ---- state ----------------------------------------------------------------------------------------------

    private List<int[]> buildGroups() {
        if (!spread || mode == Mode.SCROLL) {
            return SpreadLayout.singles(pages.size());
        }
        return SpreadLayout.spreads(pages.size(), this::isWide, true);
    }

    private boolean isWide(int page) {
        Post post = pages.get(page);
        return post.getWidth() > post.getHeight() && post.getHeight() > 0;
    }

    private void setMode(Mode newMode) {
        mode = newMode;
        context.getUserConfig().setReaderMode(newMode.name());
        persistPreferences();
        applyMode();
    }

    private void setFit(Fit newFit) {
        fit = newFit;
        context.getUserConfig().setReaderFit(newFit.name());
        persistPreferences();
        updateToolbarLabels();
        fitPageImages();
    }

    private void setRightToLeft(boolean value) {
        rightToLeft = value;
        context.getUserConfig().setReaderRightToLeft(value);
        persistPreferences();
        updateToolbarLabels();
        if (mode == Mode.PAGED) {
            showGroup(currentGroup);
        }
    }

    private void setSpread(boolean value) {
        spread = value;
        context.getUserConfig().setReaderSpread(value);
        persistPreferences();
        applyMode();
    }

    private void setZoom(double value) {
        zoom = Math.max(1, Math.min(MAX_ZOOM, value));
        updateToolbarLabels();
        if (mode == Mode.PAGED) {
            fitPageImages();
        } else {
            stripScroll.setFitToWidth(zoom <= 1.001);
            resizeHolders();
            Platform.runLater(this::updateStrip);
        }
    }

    private void onZoomScroll(ScrollEvent event) {
        if (event.isControlDown() && event.getDeltaY() != 0) {
            setZoom(event.getDeltaY() > 0 ? zoom * ZOOM_STEP : zoom / ZOOM_STEP);
            event.consume();
        }
    }

    private void persistPreferences() {
        try {
            context.saveUserConfig();
        } catch (IOException e) {
            LOGGER.log(Level.WARNING, "Could not save the reader preferences", e);
        }
    }

    private void applyMode() {
        groups = buildGroups();
        currentGroup = SpreadLayout.groupOf(groups, current);
        viewArea.getChildren().setAll(mode == Mode.PAGED ? pagedScroll : stripScroll);
        updateToolbarLabels();
        if (mode == Mode.PAGED) {
            showGroup(currentGroup);
        } else {
            setZoom(zoom);
            Platform.runLater(() -> {
                scrollToHolder(current);
                updateStrip();
            });
        }
    }

    private void updateToolbarLabels() {
        boolean paged = mode == Mode.PAGED;
        modeButton.setText(Messages.get(paged ? "reader.modePaged" : "reader.modeScroll"));
        fitButton.setText(Messages.get(fit == Fit.PAGE ? "reader.fitPage" : "reader.fitWidth"));
        fitButton.setDisable(!paged);
        directionButton.setText(Messages.get(rightToLeft ? "reader.rtl" : "reader.ltr"));
        directionButton.setDisable(!paged);
        spreadButton.setText(Messages.get(spread ? "reader.spreadOn" : "reader.spreadOff"));
        spreadButton.setDisable(!paged);
        zoomButton.setText(Math.round(zoom * 100) + "%");
        finishedButton.setText(Messages.get(library.reading().isFinished(pool.key())
                ? "reader.finished" : "reader.markFinished"));
    }

    private void step(int delta) {
        if (mode == Mode.PAGED) {
            showGroup(Math.max(0, Math.min(currentGroup + delta, groups.size() - 1)));
        } else {
            goTo(current + delta);
        }
    }

    private void goTo(int index) {
        int target = Math.max(0, Math.min(index, pages.size() - 1));
        if (mode == Mode.PAGED) {
            showGroup(SpreadLayout.groupOf(groups, target));
        } else {
            scrollToHolder(target);
            setCurrent(target);
        }
    }

    private void setCurrent(int index) {
        current = index;
        syncing = true;
        slider.setValue(current);
        syncing = false;
        int[] group = mode == Mode.PAGED ? groups.get(currentGroup) : new int[] {current};
        counter.setText(group.length == 2
                ? Messages.get("reader.pageSpread", String.valueOf(group[0] + 1), String.valueOf(group[1] + 1),
                        String.valueOf(pages.size()))
                : Messages.get("reader.page", String.valueOf(current + 1), String.valueOf(pages.size())));
        boolean paged = mode == Mode.PAGED;
        previousButton.setDisable(paged ? currentGroup <= 0 : current <= 0);
        nextButton.setDisable(paged ? currentGroup >= groups.size() - 1 : current >= pages.size() - 1);
        setTitle(Messages.get("reader.title", pool.displayName(), String.valueOf(current + 1),
                String.valueOf(pages.size())) + titleSuffix);
        saveDelay.playFromStart();
    }

    private void saveProgress() {
        library.reading().record(pool, current, pages.size());
        updateToolbarLabels();
    }

    // ---- paged mode -----------------------------------------------------------------------------------------

    /** One page of the current view: the image, or the reason there is none. */
    private static final class PageSlot extends StackPane {

        final ImageView view = new ImageView();
        final Label message = new Label();

        PageSlot() {
            view.setPreserveRatio(true);
            view.setSmooth(true);
            message.getStyleClass().add("reader-message");
            message.setWrapText(true);
            message.setMaxWidth(420);
            getChildren().addAll(view, message);
        }

        void showLoading() {
            view.setImage(null);
            message.setText(Messages.get("reader.loading"));
            message.setVisible(true);
        }

        void show(Image image) {
            view.setImage(image);
            message.setVisible(false);
        }

        void fail(String text) {
            view.setImage(null);
            message.setText(text);
            message.setVisible(true);
        }
    }

    private void showGroup(int groupIndex) {
        currentGroup = groupIndex;
        int[] group = groups.get(groupIndex);
        setCurrent(group[0]);
        int token = ++showToken;

        // In a spread the page that comes first sits on the right when reading right to left.
        int[] order = group.length == 2 && rightToLeft ? new int[] {group[1], group[0]} : group;
        for (int slot = 0; slot < slots.length; slot++) {
            boolean used = slot < order.length;
            slots[slot].setVisible(used);
            slots[slot].setManaged(used);
            if (!used) {
                slots[slot].view.setImage(null);
                continue;
            }
            PageSlot target = slots[slot];
            int page = order[slot];
            target.showLoading();
            imageFor(page).whenComplete((image, error) -> Platform.runLater(() -> {
                if (closed || token != showToken) {
                    return;
                }
                if (error != null) {
                    target.fail(describe(error));
                } else {
                    target.show(image);
                    budget.touch(page);
                }
                fitPageImages();
            }));
        }
        pagedScroll.setVvalue(0);
        pagedScroll.setHvalue(rightToLeft ? 1 : 0);
        fitPageImages();

        for (int ahead = 1; ahead <= PREFETCH_GROUPS && groupIndex + ahead < groups.size(); ahead++) {
            for (int page : groups.get(groupIndex + ahead)) {
                imageFor(page);
            }
        }
    }

    private void fitPageImages() {
        double width = pagedScroll.getViewportBounds().getWidth();
        double height = pagedScroll.getViewportBounds().getHeight();
        if (width <= 0) {
            return;
        }
        int shown = 0;
        for (PageSlot slot : slots) {
            shown += slot.isVisible() ? 1 : 0;
        }
        shown = Math.max(1, shown);
        for (PageSlot slot : slots) {
            slot.view.setFitWidth(width / shown * zoom);
            slot.view.setFitHeight(fit == Fit.PAGE ? height * zoom : 0);
            slot.message.setMaxWidth(Math.max(120, width / shown - 24));
        }
    }

    // ---- scroll mode ----------------------------------------------------------------------------------------

    private double stripWidth() {
        return Math.max(100, stripScroll.getViewportBounds().getWidth() * zoom);
    }

    private void resizeHolders() {
        double width = stripWidth();
        holders.forEach(holder -> holder.resize(width));
    }

    private void scrollToHolder(int index) {
        double overflow = strip.getHeight() - stripScroll.getViewportBounds().getHeight();
        if (overflow <= 0) {
            return;
        }
        stripScroll.setVvalue(Math.min(1, Math.max(0, holders.get(index).getBoundsInParent().getMinY() / overflow)));
    }

    private void updateStrip() {
        if (mode != Mode.SCROLL || closed) {
            return;
        }
        double viewport = stripScroll.getViewportBounds().getHeight();
        double offset = stripScroll.getVvalue() * Math.max(0, strip.getHeight() - viewport);
        double top = offset + 2; // the page at the top of the viewport counts as the current one
        int visibleIndex = current;
        for (Holder holder : holders) {
            double holderTop = holder.getBoundsInParent().getMinY();
            double holderBottom = holder.getBoundsInParent().getMaxY();
            if (top >= holderTop && top < holderBottom) {
                visibleIndex = holder.index;
            }
            boolean near = holderBottom > offset - viewport && holderTop < offset + 2 * viewport;
            boolean far = holderBottom < offset - 3 * viewport || holderTop > offset + 4 * viewport;
            if (near) {
                holder.load();
            } else if (far) {
                holder.unload();
            }
        }
        if (visibleIndex != current) {
            setCurrent(visibleIndex);
        }
    }

    /** One page of the strip: sized from the post's dimensions so the layout does not jump while loading. */
    private final class Holder extends StackPane {

        final int index;
        final ImageView view = new ImageView();
        private final Label label = new Label();
        private boolean requested;

        Holder(int index) {
            this.index = index;
            view.setPreserveRatio(true);
            view.setSmooth(true);
            label.getStyleClass().add("reader-message");
            label.setText(String.valueOf(index + 1));
            getChildren().addAll(view, label);
        }

        boolean isLoaded() {
            return requested;
        }

        void resize(double width) {
            Post post = pages.get(index);
            double aspect = post.getWidth() > 0 && post.getHeight() > 0
                    ? (double) post.getHeight() / post.getWidth() : DEFAULT_ASPECT;
            view.setFitWidth(width);
            double height = view.getImage() != null ? width * view.getImage().getHeight() / view.getImage().getWidth()
                    : width * aspect;
            setMinHeight(height);
            setPrefHeight(height);
            setMaxHeight(height);
            setMinWidth(width);
            setPrefWidth(width);
            setMaxWidth(width);
        }

        void load() {
            if (requested) {
                return;
            }
            requested = true;
            imageFor(index).whenComplete((image, error) -> Platform.runLater(() -> {
                if (closed || !requested) {
                    return;
                }
                if (error != null) {
                    label.setText(describe(error));
                    return;
                }
                view.setImage(image);
                label.setVisible(false);
                budget.touch(index);
                resize(stripWidth());
            }));
        }

        void unload() {
            if (!requested) {
                return;
            }
            requested = false;
            view.setImage(null);
            label.setVisible(true);
            label.setText(String.valueOf(index + 1));
            forget(index);
        }
    }

    // ---- loading and memory ---------------------------------------------------------------------------------

    private CompletableFuture<Image> imageFor(int index) {
        CompletableFuture<Image> existing = loads.get(index);
        if (existing != null) {
            return existing;
        }
        String url = pages.get(index).getFileUrl();
        CompletableFuture<Image> future = url == null
                ? CompletableFuture.failedFuture(new IOException(Messages.get("viewer.noFile")))
                : context.getCache().fetch(url).thenApplyAsync(file -> {
                    String type = CacheManager.detectExtension(file, "");
                    if (!IMAGE_TYPES.contains(type)) {
                        throw new CompletionException(new IOException(
                                Messages.get("reader.unsupported", type.isEmpty() ? "?" : type)));
                    }
                    Image image = new Image(file.toUri().toString(), MAX_DECODED_SIDE, MAX_DECODED_SIDE,
                            true, true, false);
                    if (image.isError()) {
                        throw new CompletionException(image.getException());
                    }
                    return image;
                }, decoder);
        loads.put(index, future);
        future.whenComplete((image, error) -> Platform.runLater(() -> {
            if (error != null) {
                loads.remove(index, future); // a failed page must be retryable
            } else if (loads.get(index) == future && !closed) {
                budget.add(index, PageBudget.estimate(image.getWidth(), image.getHeight()));
                enforceBudget();
            }
        }));
        return future;
    }

    /** Pages the reader shows or will show next; these are never dropped to save memory. */
    private Set<Integer> protectedPages() {
        Set<Integer> keep = new HashSet<>();
        if (mode == Mode.PAGED) {
            for (int g = Math.max(0, currentGroup - 1); g <= Math.min(groups.size() - 1, currentGroup + PREFETCH_GROUPS);
                 g++) {
                for (int page : groups.get(g)) {
                    keep.add(page);
                }
            }
        } else {
            holders.stream().filter(Holder::isLoaded).forEach(holder -> keep.add(holder.index));
        }
        return keep;
    }

    private void enforceBudget() {
        for (int page : budget.evict(protectedPages())) {
            loads.remove(page);
            if (mode == Mode.SCROLL) {
                holders.get(page).unload();
            }
        }
    }

    private void forget(int index) {
        loads.remove(index);
        budget.remove(index);
    }

    private static String describe(Throwable error) {
        Throwable cause = error instanceof CompletionException && error.getCause() != null ? error.getCause() : error;
        return Messages.get("reader.loadError", cause.getMessage() == null
                ? cause.getClass().getSimpleName() : cause.getMessage());
    }

    // ---- keyboard -------------------------------------------------------------------------------------------

    private void onKey(KeyEvent event) {
        KeyCode code = event.getCode();
        boolean handled = true;
        switch (code) {
            case LEFT, A -> {
                if (mode == Mode.PAGED) {
                    step(rightToLeft ? 1 : -1);
                } else {
                    handled = false;
                }
            }
            case RIGHT, D -> {
                if (mode == Mode.PAGED) {
                    step(rightToLeft ? -1 : 1);
                } else {
                    handled = false;
                }
            }
            case PAGE_UP -> pageScroll(-1);
            case PAGE_DOWN -> pageScroll(1);
            case SPACE -> pageScroll(event.isShiftDown() ? -1 : 1);
            case HOME -> goTo(0);
            case END -> goTo(pages.size() - 1);
            case M -> setMode(mode == Mode.PAGED ? Mode.SCROLL : Mode.PAGED);
            case W -> {
                if (mode == Mode.PAGED) {
                    setFit(fit == Fit.PAGE ? Fit.WIDTH : Fit.PAGE);
                }
            }
            case S -> {
                if (mode == Mode.PAGED) {
                    setSpread(!spread);
                }
            }
            case R -> setRightToLeft(!rightToLeft);
            case PLUS, EQUALS, ADD -> setZoom(zoom * ZOOM_STEP);
            case MINUS, SUBTRACT -> setZoom(zoom / ZOOM_STEP);
            case DIGIT0, NUMPAD0 -> setZoom(1);
            case F, F11 -> setFullScreen(!isFullScreen());
            case ESCAPE -> {
                if (isFullScreen()) {
                    setFullScreen(false);
                } else {
                    close();
                }
            }
            default -> handled = false;
        }
        if (handled) {
            event.consume();
        }
    }

    /** Scrolls a screenful; in paged mode the next page opens once the bottom of the current one is reached. */
    private void pageScroll(int direction) {
        ScrollPane scroll = mode == Mode.PAGED ? pagedScroll : stripScroll;
        Node content = scroll.getContent();
        double overflow = content.getBoundsInLocal().getHeight() - scroll.getViewportBounds().getHeight();
        if (overflow <= 1) {
            if (mode == Mode.PAGED) {
                step(direction);
            }
            return;
        }
        double amount = 0.9 * scroll.getViewportBounds().getHeight() / overflow;
        double target = scroll.getVvalue() + direction * amount;
        if (mode == Mode.PAGED && (direction > 0 && scroll.getVvalue() >= 0.999
                || direction < 0 && scroll.getVvalue() <= 0.001)) {
            step(direction);
            return;
        }
        scroll.setVvalue(Math.max(0, Math.min(1, target)));
    }
}
