package com.ayoub.xonotic;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.ProgressBar;
import android.widget.TextView;

/** Verifies/copies local APK assets, then starts the engine. No HTTP client. */
public class LauncherActivity extends Activity {
    private TextView status;
    private ProgressBar progress;
    private Button action;
    private InstallTask task;
    private boolean launched;

    @Override protected void onCreate(Bundle state) {
        super.onCreate(state);
        setContentView(R.layout.activity_launcher);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        status = findViewById(R.id.status);
        progress = findViewById(R.id.progress);
        action = findViewById(R.id.action);
        task = (InstallTask) getLastNonConfigurationInstance();
        if (task == null) task = new InstallTask(getApplicationContext());
        task.owner = this;
        action.setOnClickListener(v -> {
            task = new InstallTask(getApplicationContext());
            task.owner = this;
            task.start();
        });
        if (!task.started) task.start();
        render();
    }

    @Override public Object onRetainNonConfigurationInstance() {
        task.owner = null;
        return task;
    }
    @Override protected void onDestroy() {
        if (task.owner == this) task.owner = null;
        super.onDestroy();
    }
    private void render() {
        if (isFinishing() || isDestroyed()) return;
        progress.setVisibility(ProgressBar.VISIBLE);
        progress.setProgress(task.percent);
        status.setText(task.error == null
                ? getString(R.string.preparing, task.percent / 10) + "\n" + task.name
                : getString(R.string.failed, task.error));
        action.setText(R.string.retry);
        action.setVisibility(task.error == null ? Button.GONE : Button.VISIBLE);
        if (task.complete && !launched) {
            launched = true;
            startActivity(new Intent(this, XonoticActivity.class));
            finish();
        }
    }
    private static final class InstallTask {
        final Context context;
        final Handler ui = new Handler(Looper.getMainLooper());
        LauncherActivity owner;
        boolean started, complete;
        int percent;
        String name = "", error;
        InstallTask(Context context) { this.context = context; }
        void start() {
            started = true;
            if (owner != null) owner.render();
            new Thread(() -> {
                try {
                    GameData.install(context, (file, done, total) -> ui.post(() -> {
                        name = file; percent = (int) (done * 1000 / total);
                        if (owner != null) owner.render();
                    }));
                    ui.post(() -> { complete = true; if (owner != null) owner.render(); });
                } catch (Exception e) {
                    ui.post(() -> { error = e.getMessage(); if (owner != null) owner.render(); });
                }
            }, "xonotic-offline-setup").start();
        }
    }
}
