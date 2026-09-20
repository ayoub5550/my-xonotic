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
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        // Launcher verifies every resource, including touch assets, before entering here.
        // This activity is not exported; do not continue SDL startup after a failed setup.
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

}
