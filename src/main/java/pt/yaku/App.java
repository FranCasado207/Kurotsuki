package pt.yaku;

import java.io.IOException;

import javafx.application.Application;
import javafx.scene.Scene;
import javafx.stage.Stage;
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
        MainView view = new MainView(stage, context);

        Scene scene = new Scene(view.getRoot(), 1100, 700);

        stage.setScene(scene);
        stage.show();
    }

    public static void main(String[] args) {
        launch(args);
    }
}
