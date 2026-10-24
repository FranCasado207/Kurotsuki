package pt.yaku.user.config;

import java.io.BufferedReader;
import java.io.BufferedWriter;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.nio.file.attribute.PosixFilePermission;
import java.nio.file.attribute.PosixFilePermissions;
import java.util.ArrayList;
import java.util.Iterator;
import java.util.Set;
import java.util.logging.Level;
import java.util.logging.Logger;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;

public class ConfigManager {

    private static final String CONFIG_FILE = "config.json";
    private static final String TEMP_FILE = "config.json.tmp";

    private static final Logger LOGGER = Logger.getLogger(ConfigManager.class.getName());
    private static final Set<PosixFilePermission> OWNER_ONLY_FILE = PosixFilePermissions.fromString("rw-------");
    private static final Set<PosixFilePermission> OWNER_ONLY_DIRECTORY = PosixFilePermissions.fromString("rwx------");

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
                LOGGER.warning("Booru configuration with unknown type ignored.");
                it.remove();
                continue;
            }
            try {
                booru.validate();
                if (booru.sendsCredentialsInClear()) {
                    LOGGER.warning("Credentials for " + booru.getBaseUrl() + " are sent over plain http.");
                }
            } catch (IllegalStateException e) {
                LOGGER.warning("Invalid booru configuration ignored: " + e.getMessage());
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
        restrictToOwner(folderPath);

        // The file holds API keys: create it owner-only from the start instead of tightening it afterwards.
        Files.deleteIfExists(tempPath);
        try {
            Files.createFile(tempPath, PosixFilePermissions.asFileAttribute(OWNER_ONLY_FILE));
        } catch (UnsupportedOperationException e) {
            Files.createFile(tempPath);
        }
        try (BufferedWriter writer = Files.newBufferedWriter(tempPath)) {
            gson.toJson(userConfig, writer);
        }
        Files.move(tempPath, filePath, StandardCopyOption.REPLACE_EXISTING, StandardCopyOption.ATOMIC_MOVE);
    }

    /** Best effort: drops group/other access on POSIX systems, does nothing elsewhere. */
    public static void restrictToOwner(Path path) {
        try {
            Set<PosixFilePermission> permissions = Files.isDirectory(path) ? OWNER_ONLY_DIRECTORY : OWNER_ONLY_FILE;
            Files.setPosixFilePermissions(path, permissions);
        } catch (UnsupportedOperationException | IOException e) {
            LOGGER.log(Level.FINE, "Could not restrict permissions of " + path, e);
        }
    }

    public static Path getFolderPath() {
        return AppPaths.get().config();
    }
}
