package pt.yaku;

import java.io.IOException;

import javafx.application.Application;
import javafx.scene.Scene;
import javafx.scene.image.Image;
import javafx.stage.Stage;
import atlantafx.base.theme.PrimerDark;
import pt.yaku.cache.CacheManager;
import pt.yaku.context.ApplicationContext;
import pt.yaku.i18n.Messages;
import pt.yaku.ui.MainView;

public class App extends Application {
    private ApplicationContext context;

    @Override
    public void init() throws IOException {
        context = new ApplicationContext();
        Messages.setLanguage(context.getUserConfig().getLanguage());
        CacheManager.init(context);
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

    public static void main(String[] args) {
        launch(args);
    }
}
