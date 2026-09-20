package com.ayoub.xonotic;

import android.content.Context;
import java.io.*;
import java.util.List;

/** All game resources are bundled; no network resource download exists. */
public final class GameData {
    private GameData() {}
    public static File baseDir(Context ctx) {
        File ext = ctx.getExternalFilesDir(null);
        return ext != null ? ext : ctx.getFilesDir();
    }
    public static File dataDir(Context ctx) { return new File(baseDir(ctx), "data"); }
    public static File userDir(Context ctx) { return new File(baseDir(ctx), "userdata"); }
    public static List<OfflineInstaller.Entry> manifest(Context ctx) throws IOException {
        return OfflineInstaller.readManifest(ctx.getAssets().open("game-manifest.tsv"));
    }
    public static void install(Context ctx, OfflineInstaller.Progress progress) throws IOException {
        OfflineInstaller.install(name -> ctx.getAssets().open(name), manifest(ctx), dataDir(ctx), progress);
        File user = userDir(ctx);
        if (!user.isDirectory() && !user.mkdirs()) throw new IOException("Cannot create settings storage");
    }
}
