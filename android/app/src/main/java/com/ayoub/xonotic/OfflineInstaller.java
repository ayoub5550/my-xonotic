package com.ayoub.xonotic;

import java.io.*;
import java.nio.file.Files;
import java.nio.file.StandardCopyOption;
import java.security.MessageDigest;
import java.util.*;

/** Platform-independent, resumable installer for APK-bundled resources. */
public final class OfflineInstaller {
    public interface Assets { InputStream open(String name) throws IOException; }
    public interface Progress { void update(String name, long done, long total); }
    public static final class Entry {
        public final String name, sha256;
        public final long size;
        Entry(String name, long size, String sha256) {
            this.name = name; this.size = size; this.sha256 = sha256;
        }
    }

    public static List<Entry> readManifest(InputStream input) throws IOException {
        List<Entry> result = new ArrayList<>();
        Set<String> names = new HashSet<>();
        try (BufferedReader r = new BufferedReader(new InputStreamReader(input, "UTF-8"))) {
            String line;
            while ((line = r.readLine()) != null) {
                String[] f = line.split("\t");
                if (f.length != 3 || !f[0].matches("[A-Za-z0-9_.-]+\\.pk3")
                        || !f[2].matches("[a-f0-9]{64}") || !names.add(f[0]))
                    throw new IOException("Invalid bundled resource manifest");
                long size;
                try { size = Long.parseLong(f[1]); }
                catch (NumberFormatException e) { throw new IOException("Invalid resource size", e); }
                if (size <= 0) throw new IOException("Invalid resource size");
                result.add(new Entry(f[0], size, f[2]));
            }
        }
        if (result.isEmpty()) throw new IOException("Empty bundled resource manifest");
        return result;
    }

    private static MessageDigest digest() {
        try { return MessageDigest.getInstance("SHA-256"); }
        catch (Exception e) { throw new AssertionError(e); }
    }
    private static String hex(byte[] bytes) {
        StringBuilder s = new StringBuilder();
        for (byte b : bytes) s.append(String.format(Locale.ROOT, "%02x", b & 255));
        return s.toString();
    }
    public static boolean valid(File file, Entry e) throws IOException {
        if (!file.isFile() || file.length() != e.size) return false;
        MessageDigest d = digest();
        try (InputStream in = new FileInputStream(file)) {
            byte[] buf = new byte[262144];
            int n;
            while ((n = in.read(buf)) != -1) d.update(buf, 0, n);
        }
        return hex(d.digest()).equals(e.sha256);
    }
    public static synchronized void install(Assets assets, List<Entry> entries, File dir, Progress progress)
            throws IOException {
        if (!dir.isDirectory() && !dir.mkdirs()) throw new IOException("Cannot create game storage");
        long total = 0, done = 0;
        for (Entry e : entries) total += e.size;
        for (Entry e : entries) {
            File target = new File(dir, e.name);
            File part = new File(dir, e.name + ".part");
            Files.deleteIfExists(part.toPath());
            progress.update(e.name, done, total);
            if (valid(target, e)) {
                done += e.size;
                progress.update(e.name, done, total);
                continue;
            }
            if (dir.getUsableSpace() < e.size + (64L << 20))
                throw new IOException("Not enough free space. Free at least "
                        + ((e.size + (64L << 20)) / (1024 * 1024)) + " MB and retry.");
            MessageDigest d = digest();
            long count = 0, lastReport = 0;
            try {
                try (InputStream in = assets.open("game/" + e.name);
                     FileOutputStream out = new FileOutputStream(part)) {
                    byte[] buf = new byte[262144];
                    int n;
                    while ((n = in.read(buf)) != -1) {
                        count += n;
                        if (count > e.size) throw new IOException("Unexpected resource size: " + e.name);
                        out.write(buf, 0, n); d.update(buf, 0, n);
                        if (count - lastReport >= (4L << 20)) {
                            progress.update(e.name, done + count, total);
                            lastReport = count;
                        }
                    }
                    out.getFD().sync();
                }
                if (count != e.size || !hex(d.digest()).equals(e.sha256))
                    throw new IOException("Resource verification failed: " + e.name);
                Files.move(part.toPath(), target.toPath(), StandardCopyOption.REPLACE_EXISTING,
                        StandardCopyOption.ATOMIC_MOVE);
            } finally {
                Files.deleteIfExists(part.toPath());
            }
            done += e.size;
            progress.update(e.name, done, total);
        }
    }
}
