package com.ayoub.xonotic;

import android.content.Intent;
import android.content.res.AssetManager;
import android.os.Bundle;
import android.util.Log;

import org.libsdl.app.SDLActivity;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.util.ArrayList;
import java.util.List;

/** Hosts the DarkPlaces engine (libmain.so, SDL2) with Xonotic's game data. */
public class XonoticActivity extends SDLActivity {
    private static final String TAG = "Xonotic";
    /** Bump when the bundled touch-control pk3 changes so it gets re-copied. */
    private static final String ANDROID_PK3 = "zz-xonotic-android-touch.pk3";
    private static final String ANDROID_PK3_VERSION = "1";

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        if (!GameData.isInstalled(this)) {
            // Data vanished (user cleared storage): go back to the launcher.
            Intent i = new Intent(this, LauncherActivity.class);
            i.putExtra("returned", true);
            startActivity(i);
            finish();
        }
        installBundledPk3();
        GameData.userDir(this).mkdirs();
        super.onCreate(savedInstanceState);
    }

    @Override
    protected String[] getLibraries() {
        // libpng is dlopen()ed by the engine by name; loading it here first makes
        // the bare-name lookup resolve to the bundled copy.
        return new String[] { "SDL2", "png", "main" };
    }

    @Override
    protected String[] getArguments() {
        List<String> args = new ArrayList<>();
        args.add("-xonotic");
        args.add("-basedir");
        args.add(GameData.baseDir(this).getAbsolutePath());
        args.add("-userdir");
        args.add(GameData.userDir(this).getAbsolutePath());
        // Mobile-friendly defaults; users can still change them in the menu.
        args.add("+vid_touchscreen"); args.add("1");
        args.add("+vid_fullscreen"); args.add("1");
        args.add("+vid_conwidth"); args.add("1024");
        args.add("+vid_conheight"); args.add("576");
        args.add("+exec"); args.add("android.cfg");
        return args.toArray(new String[0]);
    }

    /** Copies assets/<ANDROID_PK3> into the data directory (touch icons, mobile cfg). */
    private void installBundledPk3() {
        File dataDir = GameData.dataDir(this);
        File target = new File(dataDir, ANDROID_PK3);
        File stamp = new File(dataDir, ANDROID_PK3 + ".v" + ANDROID_PK3_VERSION);
        if (target.isFile() && stamp.isFile()) return;
        AssetManager am = getAssets();
        try (InputStream in = am.open(ANDROID_PK3); OutputStream out = new FileOutputStream(target)) {
            byte[] buf = new byte[1 << 16];
            int n;
            while ((n = in.read(buf)) > 0) out.write(buf, 0, n);
            stamp.createNewFile();
            Log.i(TAG, "installed " + ANDROID_PK3);
        } catch (IOException e) {
            Log.w(TAG, "could not install " + ANDROID_PK3 + ": " + e);
        }
    }
}
