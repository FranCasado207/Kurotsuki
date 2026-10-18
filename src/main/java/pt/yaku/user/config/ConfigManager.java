package pt.yaku.user.config;

import java.io.BufferedReader;
import java.io.BufferedWriter;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.nio.file.StandardCopyOption;
import java.util.ArrayList;
import java.util.Iterator;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;

public class ConfigManager {

    private static final String APP_FOLDER = "kurotsuki";
    private static final String CONFIG_FILE = "config.json";
    private static final String TEMP_FILE = "config.json.tmp";

    private static final Gson gson = new GsonBuilder()
            .setPrettyPrinting()
            .registerTypeAdapter(BooruConfiguration.class, new BooruConfigurationAdapter())
            .create();

    public static UserConfiguration configLoader() throws IOException {
        Path folderPath = getFolderPath();
        Path filePath = folderPath.resolve(CONFIG_FILE);

        if (Files.notExists(folderPath)) {
            Files.createDirectories(folderPath);
        }

        if (Files.notExists(filePath)) {
            UserConfiguration userConfig = new UserConfiguration();
            saveConfig(userConfig);
            return userConfig;
        }

        try (BufferedReader reader = Files.newBufferedReader(filePath)) {
            UserConfiguration userConfig = gson.fromJson(reader, UserConfiguration.class);
            if (userConfig == null) {
                return new UserConfiguration();
            }
            return sanitize(userConfig);
        }
    }

    private static UserConfiguration sanitize(UserConfiguration userConfig) {
        if (userConfig.getBooruConfigurations() == null) {
            userConfig.setBooruConfigurations(new ArrayList<>());
            return userConfig;
        }

        Iterator<BooruConfiguration> it = userConfig.getBooruConfigurations().iterator();
        while (it.hasNext()) {
            BooruConfiguration booru = it.next();
            if (booru == null) {
                System.err.println("Booru configuration with unknown type ignored.");
                it.remove();
                continue;
            }
            try {
                booru.validate();
            } catch (IllegalStateException e) {
                System.err.println("Invalid booru configuration ignored: " + e.getMessage());
                it.remove();
            }
        }
        return userConfig;
    }

    public static void saveConfig(UserConfiguration userConfig) throws IOException {
        Path folderPath = getFolderPath();
        Path filePath = folderPath.resolve(CONFIG_FILE);
        Path tempPath = folderPath.resolve(TEMP_FILE);

        Files.createDirectories(folderPath);

        try (BufferedWriter writer = Files.newBufferedWriter(tempPath)) {
            gson.toJson(userConfig, writer);
        }
        Files.move(tempPath, filePath, StandardCopyOption.REPLACE_EXISTING, StandardCopyOption.ATOMIC_MOVE);
    }

    public static Path getFolderPath() {
        String home = System.getProperty("user.home");
        return Paths.get(home, APP_FOLDER);
    }
}
