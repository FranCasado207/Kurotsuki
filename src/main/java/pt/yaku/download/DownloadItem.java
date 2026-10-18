package pt.yaku.download;

import javafx.beans.property.DoubleProperty;
import javafx.beans.property.ObjectProperty;
import javafx.beans.property.SimpleDoubleProperty;
import javafx.beans.property.SimpleObjectProperty;
import javafx.beans.property.SimpleStringProperty;
import javafx.beans.property.StringProperty;
import pt.yaku.booru.Post;
import pt.yaku.i18n.Messages;

public class DownloadItem {

    public enum Status { QUEUED, DOWNLOADING, DONE, FAILED }

    private final Post post;
    private final ObjectProperty<Status> status = new SimpleObjectProperty<>(Status.QUEUED);
    private final DoubleProperty progress = new SimpleDoubleProperty(0);
    private final StringProperty message = new SimpleStringProperty("");
    private final StringProperty label = new SimpleStringProperty();

    public DownloadItem(Post post) {
        this.post = post;
        status.addListener((obs, oldValue, newValue) -> updateLabel());
        progress.addListener((obs, oldValue, newValue) -> updateLabel());
        message.addListener((obs, oldValue, newValue) -> updateLabel());
        updateLabel();
    }

    public Post getPost() {
        return post;
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
            case FAILED -> Messages.get("download.failed", message.get());
        });
    }
}
