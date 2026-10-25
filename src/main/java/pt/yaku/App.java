package pt.yaku;

import java.io.IOException;
import java.util.logging.Logger;

import javafx.application.Application;
import javafx.scene.Scene;
import javafx.scene.image.Image;
import javafx.stage.Stage;
import atlantafx.base.theme.PrimerDark;
import pt.yaku.cache.CacheManager;
import pt.yaku.context.ApplicationContext;
import pt.yaku.i18n.Messages;
import pt.yaku.log.AppLog;
import pt.yaku.log.LogArchive;
import pt.yaku.log.SystemInfo;
import pt.yaku.user.config.AppPaths;
import pt.yaku.ui.MainView;

public class App extends Application {
    private static final Logger LOGGER = Logger.getLogger(App.class.getName());

    private ApplicationContext context;

    @Override
    public void init() throws IOException {
        AppLog.install();
        context = new ApplicationContext();
        LOGGER.info("Started: " + SystemInfo.describe(context.getUserConfig().getBooruConfigurations()).replace('\n', ';'));
        Messages.setLanguage(context.getUserConfig().getLanguage());
        Thread trimmer = new Thread(() -> {
            try {
                context.getCache().trim(CacheManager.DEFAULT_MAX_BYTES);
            } catch (IOException e) {
                System.getLogger(App.class.getName()).log(System.Logger.Level.WARNING, "Cache trim failed", e);
            }
        }, "cache-trim");
        trimmer.setDaemon(true);
        trimmer.start();
    }

    @Override
    public void start(Stage stage) {
        Application.setUserAgentStylesheet(new PrimerDark().getUserAgentStylesheet());
        MainView view = new MainView(stage, context);

        Scene scene = new Scene(view.getRoot(), 1100, 700);
        scene.getStylesheets().add(getClass().getResource("/style.css").toExternalForm());

        stage.getIcons().add(new Image(getClass().getResourceAsStream("/icons/kurotsuki-256.png")));
        stage.setScene(scene);
        stage.show();
    }

    @Override
    public void stop() {
        LOGGER.info("Stopping");
        try {
            LogArchive.write(AppLog.directory(), AppPaths.get().data().resolve(LogArchive.FILE_NAME),
                    SystemInfo.describe(context.getUserConfig().getBooruConfigurations()));
        } catch (IOException | RuntimeException e) {
            System.err.println("Could not write the log archive: " + e.getMessage());
        }
    }

    public static void main(String[] args) {
        launch(args);
    }
}
