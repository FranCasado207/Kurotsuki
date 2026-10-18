package pt.yaku.ui;

import java.nio.file.Path;

import javafx.geometry.Insets;
import javafx.geometry.Pos;
import javafx.scene.control.Button;
import javafx.scene.control.Label;
import javafx.scene.control.Slider;
import javafx.scene.layout.BorderPane;
import javafx.scene.layout.HBox;
import javafx.scene.layout.Priority;
import javafx.scene.layout.StackPane;
import javafx.scene.media.Media;
import javafx.scene.media.MediaPlayer;
import javafx.scene.media.MediaView;
import javafx.util.Duration;
import pt.yaku.i18n.Messages;

public class VideoPlayer extends BorderPane {

    private static final double SKIP_SECONDS = 10;

    private final MediaPlayer player;
    private final Media media;
    private final Button playButton = new Button(Messages.get("player.play"));
    private final Button backButton = new Button(Messages.get("player.back"));
    private final Button forwardButton = new Button(Messages.get("player.forward"));
    private final Slider seekSlider = new Slider(0, 1, 0);
    private final Label timeLabel = new Label("--:-- / --:--");
    private final Button muteButton = new Button(Messages.get("player.mute"));
    private final Slider volumeSlider = new Slider(0, 1, 1);
    private boolean updatingFromPlayer;

    public VideoPlayer(Path file, Runnable onError) {
        media = new Media(file.toUri().toString());
        player = new MediaPlayer(media);
        player.setCycleCount(MediaPlayer.INDEFINITE);
        player.setAutoPlay(true);
        player.setOnError(onError);
        media.setOnError(onError);

        MediaView view = new MediaView(player);
        view.setPreserveRatio(true);
        StackPane screen = new StackPane(view);
        view.fitWidthProperty().bind(screen.widthProperty().subtract(20));
        view.fitHeightProperty().bind(screen.heightProperty().subtract(20));
        screen.setOnMouseClicked(event -> togglePlay());

        setCenter(screen);
        setBottom(buildControls());
        bindPlayer();
    }

    public void dispose() {
        player.dispose();
    }

    private HBox buildControls() {
        seekSlider.setMaxWidth(Double.MAX_VALUE);
        HBox.setHgrow(seekSlider, Priority.ALWAYS);
        volumeSlider.setPrefWidth(100);

        playButton.setOnAction(event -> togglePlay());
        backButton.setOnAction(event -> skip(-SKIP_SECONDS));
        forwardButton.setOnAction(event -> skip(SKIP_SECONDS));
        muteButton.setOnAction(event -> player.setMute(!player.isMute()));

        HBox controls = new HBox(8, playButton, backButton, forwardButton,
                seekSlider, timeLabel, muteButton, volumeSlider);
        controls.setAlignment(Pos.CENTER_LEFT);
        controls.setPadding(new Insets(8));
        return controls;
    }

    private void bindPlayer() {
        volumeSlider.valueProperty().bindBidirectional(player.volumeProperty());

        player.statusProperty().addListener((obs, oldStatus, status) ->
                playButton.setText(status == MediaPlayer.Status.PLAYING ? Messages.get("player.pause") : Messages.get("player.play")));

        player.muteProperty().addListener((obs, wasMuted, muted) ->
                muteButton.setText(muted ? Messages.get("player.unmute") : Messages.get("player.mute")));

        player.currentTimeProperty().addListener((obs, oldTime, now) -> {
            updatingFromPlayer = true;
            seekSlider.setValue(fraction(now));
            updatingFromPlayer = false;
            updateTimeLabel();
        });

        media.durationProperty().addListener((obs, oldTotal, total) -> updateTimeLabel());

        seekSlider.valueProperty().addListener((obs, oldValue, value) -> {
            Duration total = mediaDuration();
            if (!updatingFromPlayer && total != null && !total.isUnknown()) {
                player.seek(total.multiply(value.doubleValue()));
            }
        });
    }

    private Duration mediaDuration() {
        return player.getMedia().getDuration();
    }

    private void togglePlay() {
        if (player.getStatus() == MediaPlayer.Status.PLAYING) {
            player.pause();
        } else {
            player.play();
        }
    }

    private void skip(double seconds) {
        Duration total = mediaDuration();
        double target = player.getCurrentTime().toSeconds() + seconds;
        if (total != null && !total.isUnknown()) {
            target = Math.min(target, total.toSeconds());
        }
        player.seek(Duration.seconds(Math.max(0, target)));
    }

    private double fraction(Duration now) {
        Duration total = mediaDuration();
        if (now == null || total == null || total.isUnknown() || total.toMillis() == 0) {
            return 0;
        }
        return now.toMillis() / total.toMillis();
    }

    private void updateTimeLabel() {
        timeLabel.setText(format(player.getCurrentTime()) + " / " + format(mediaDuration()));
    }

    private static String format(Duration duration) {
        if (duration == null || duration.isUnknown()) {
            return "--:--";
        }
        long seconds = (long) duration.toSeconds();
        long hours = seconds / 3600;
        long minutes = (seconds % 3600) / 60;
        long secs = seconds % 60;
        return hours > 0
                ? String.format("%d:%02d:%02d", hours, minutes, secs)
                : String.format("%02d:%02d", minutes, secs);
    }
}
