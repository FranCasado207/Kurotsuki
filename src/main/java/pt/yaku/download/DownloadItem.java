package pt.yaku.download;

import javafx.beans.property.DoubleProperty;
import javafx.beans.property.ObjectProperty;
import javafx.beans.property.SimpleDoubleProperty;
import javafx.beans.property.SimpleObjectProperty;
import javafx.beans.property.SimpleStringProperty;
import javafx.beans.property.StringProperty;
import pt.yaku.booru.Pool;
import pt.yaku.booru.Post;
import pt.yaku.i18n.Messages;

public class DownloadItem {

    public enum Status { QUEUED, DOWNLOADING, DONE, FAILED, SKIPPED }

    private final Post post;
    private final Pool pool;
    private final ObjectProperty<Status> status = new SimpleObjectProperty<>(Status.QUEUED);
    private final DoubleProperty progress = new SimpleDoubleProperty(0);
    private final StringProperty message = new SimpleStringProperty("");
    private final StringProperty label = new SimpleStringProperty();
    private String payload;

    public DownloadItem(Post post) {
        this(post, null);
    }

    /** A whole pool, saved as one comic archive. */
    public DownloadItem(Pool pool) {
        this(null, pool);
    }

    private DownloadItem(Post post, Pool pool) {
        this.post = post;
        this.pool = pool;
        status.addListener((obs, oldValue, newValue) -> updateLabel());
        progress.addListener((obs, oldValue, newValue) -> updateLabel());
        message.addListener((obs, oldValue, newValue) -> updateLabel());
        updateLabel();
    }

    /** The post for a single-file download, or null when this item is a pool. */
    public Post getPost() {
        return post;
    }

    public Pool getPool() {
        return pool;
    }

    public String key() {
        return pool != null ? pool.key() : post.key();
    }

    public String title() {
        return pool != null
                ? Messages.get("download.pool", pool.displayName(), String.valueOf(pool.postCount()))
                : post.getId() + " - " + post.getImage();
    }

    /** The JSON saved for the persistent queue, cached because it never changes. */
    String getPayload() {
        return payload;
    }

    void setPayload(String value) {
        payload = value;
    }

    public Status getStatus() {
        return status.get();
    }

    public void setStatus(Status value) {
        status.set(value);
    }

    public ObjectProperty<Status> statusProperty() {
        return status;
    }

    public double getProgress() {
        return progress.get();
    }

    public void setProgress(double value) {
        progress.set(value);
    }

    public DoubleProperty progressProperty() {
        return progress;
    }

    public void setMessage(String value) {
        message.set(value);
    }

    public StringProperty labelProperty() {
        return label;
    }

    private void updateLabel() {
        label.set(switch (status.get()) {
            case QUEUED -> Messages.get("download.queued");
            case DOWNLOADING -> Messages.get("download.downloading", String.valueOf(Math.round(progress.get() * 100)));
            case DONE -> Messages.get("download.done");
            case SKIPPED -> Messages.get("download.skipped");
            case FAILED -> Messages.get("download.failed", message.get());
        });
    }
}
