package com.ayoub.xonotic;

import android.app.Activity;
import android.content.Intent;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.widget.Button;
import android.widget.ProgressBar;
import android.widget.TextView;

import java.io.BufferedInputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.util.Arrays;
import java.util.HashSet;
import java.util.Set;
import java.util.zip.ZipEntry;
import java.util.zip.ZipInputStream;

/**
 * First-run screen: checks that the Xonotic pk3 files are present, offers to
 * download the official release archive and stream-extract the pk3s out of it,
 * then starts the engine activity. The game data is not bundled in the APK
 * (about 1 GB) — it is fetched once from dl.xonotic.org.
 */
public class LauncherActivity extends Activity {
    private TextView status;
    private ProgressBar progress;
    private Button action;
    private final Handler ui = new Handler(Looper.getMainLooper());
    private volatile boolean working;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_launcher);
        status = findViewById(R.id.status);
        progress = findViewById(R.id.progress);
        action = findViewById(R.id.action);
        refresh();
    }

    @Override
    protected void onResume() {
        super.onResume();
        if (!working) refresh();
    }

    private void refresh() {
        if (GameData.isInstalled(this)) {
            if (getIntent().getBooleanExtra("returned", false)) {
                status.setText(R.string.play);
                action.setText(R.string.play);
                action.setOnClickListener(v -> startGame());
            } else {
                startGame();
            }
            return;
        }
        status.setText(getString(R.string.data_missing, GameData.dataDir(this).getAbsolutePath()));
        action.setText(R.string.download);
        action.setOnClickListener(v -> startDownload());
    }

    private void startGame() {
        Intent i = new Intent(this, XonoticActivity.class);
        startActivity(i);
        finish();
    }

    private void startDownload() {
        working = true;
        action.setEnabled(false);
        progress.setVisibility(ProgressBar.VISIBLE);
        new Thread(this::downloadAndExtract, "xonotic-data").start();
    }

    private void post(Runnable r) { ui.post(r); }

    private void downloadAndExtract() {
        File dataDir = GameData.dataDir(this);
        dataDir.mkdirs();
        Set<String> wanted = new HashSet<>(Arrays.asList(GameData.REQUIRED));
        wanted.addAll(Arrays.asList(GameData.OPTIONAL));
        HttpURLConnection conn = null;
        try {
            conn = (HttpURLConnection) new URL(GameData.RELEASE_URL).openConnection();
            conn.setConnectTimeout(20000);
            conn.setReadTimeout(60000);
            conn.connect();
            if (conn.getResponseCode() != 200) throw new IOException("HTTP " + conn.getResponseCode());
            long total = conn.getContentLengthLong();
            if (total <= 0) total = GameData.RELEASE_SIZE;
            final long totalMb = total / (1024 * 1024);
            CountingInputStream counting = new CountingInputStream(new BufferedInputStream(conn.getInputStream(), 1 << 16));
            ZipInputStream zip = new ZipInputStream(counting);
            ZipEntry entry;
            byte[] buf = new byte[1 << 16];
            long lastReport = 0;
            while ((entry = zip.getNextEntry()) != null) {
                String name = entry.getName();
                String base = name.substring(name.lastIndexOf('/') + 1);
                boolean take = !entry.isDirectory() && name.contains("/data/") && wanted.contains(base);
                if (take) {
                    final String shown = base;
                    post(() -> status.setText(getString(R.string.extracting, shown)));
                    File out = new File(dataDir, base + ".part");
                    try (OutputStream os = new FileOutputStream(out)) {
                        int n;
                        while ((n = zip.read(buf)) > 0) {
                            os.write(buf, 0, n);
                            long read = counting.count;
                            if (read - lastReport > (4L << 20)) {
                                lastReport = read;
                                final long mb = read / (1024 * 1024);
                                final int pct = (int) Math.min(1000, read * 1000 / total);
                                post(() -> {
                                    progress.setProgress(pct);
                                    status.setText(getString(R.string.downloading, mb, totalMb) + "\n" + shown);
                                });
                            }
                        }
                    }
                    File finalFile = new File(dataDir, base);
                    finalFile.delete();
                    if (!out.renameTo(finalFile)) throw new IOException("rename failed: " + base);
                } else {
                    // Skip quickly but keep the progress bar alive.
                    while (zip.read(buf) > 0) {
                        long read = counting.count;
                        if (read - lastReport > (8L << 20)) {
                            lastReport = read;
                            final long mb = read / (1024 * 1024);
                            final int pct = (int) Math.min(1000, read * 1000 / total);
                            post(() -> { progress.setProgress(pct); status.setText(getString(R.string.downloading, mb, totalMb)); });
                        }
                    }
                }
                zip.closeEntry();
            }
            zip.close();
            post(() -> {
                working = false;
                progress.setVisibility(ProgressBar.GONE);
                action.setEnabled(true);
                refresh();
            });
        } catch (Exception e) {
            final String msg = e.getClass().getSimpleName() + ": " + e.getMessage();
            post(() -> {
                working = false;
                action.setEnabled(true);
                status.setText(getString(R.string.failed, msg) + "\n\n" + getString(R.string.data_missing, dataDir.getAbsolutePath()));
            });
        } finally {
            if (conn != null) conn.disconnect();
        }
    }

    /** Counts bytes pulled through it so download progress can be shown while extracting. */
    static final class CountingInputStream extends InputStream {
        private final InputStream in;
        volatile long count;

        CountingInputStream(InputStream in) { this.in = in; }

        @Override public int read() throws IOException {
            int b = in.read();
            if (b >= 0) count++;
            return b;
        }

        @Override public int read(byte[] b, int off, int len) throws IOException {
            int n = in.read(b, off, len);
            if (n > 0) count += n;
            return n;
        }

        @Override public void close() throws IOException { in.close(); }
    }
}
