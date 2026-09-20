package com.ayoub.xonotic;

import android.content.Context;

import java.io.File;

/** Where the engine expects Xonotic's pk3 files and which ones are mandatory. */
public final class GameData {
    /** Official 0.8.6 release archive (GPL). Only the pk3 files inside are extracted. */
    public static final String RELEASE_URL = "https://dl.xonotic.org/xonotic-0.8.6.zip";
    public static final long RELEASE_SIZE = 1238439495L;

    /** pk3 files required to boot the game; music/compat packs are optional. */
    public static final String[] REQUIRED = {
            "xonotic-20230620-data.pk3",
            "xonotic-20230620-maps.pk3",
            "font-xolonium-20230620.pk3",
            "font-unifont-20230620.pk3",
    };
    public static final String[] OPTIONAL = {
            "xonotic-20230620-music.pk3",
            "xonotic-20230620-xoncompat.pk3",
    };

    private GameData() {}

    /** basedir: engine looks for basedir/data/*.pk3 (gamedirname1 = "data"). */
    public static File baseDir(Context ctx) {
        File ext = ctx.getExternalFilesDir(null);
        return ext != null ? ext : ctx.getFilesDir();
    }

    public static File dataDir(Context ctx) {
        return new File(baseDir(ctx), "data");
    }

    /** User config/screenshots live here (engine -userdir). */
    public static File userDir(Context ctx) {
        return new File(baseDir(ctx), "userdata");
    }

    public static boolean isInstalled(Context ctx) {
        File data = dataDir(ctx);
        for (String name : REQUIRED) {
            File f = new File(data, name);
            if (!f.isFile() || f.length() < 1024) return false;
        }
        return true;
    }
}
