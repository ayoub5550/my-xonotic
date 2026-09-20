package com.ayoub.xonotic;

import java.io.*;
import java.nio.file.*;
import java.security.*;
import java.util.*;

public class OfflineInstallerTest {
    static int assertions;
    static void check(boolean b, String message) {
        assertions++;
        if (!b) throw new AssertionError(message);
    }
    static List<OfflineInstaller.Entry> manifest(byte[] data) throws Exception {
        StringBuilder h = new StringBuilder();
        for (byte b : MessageDigest.getInstance("SHA-256").digest(data))
            h.append(String.format("%02x", b & 255));
        return OfflineInstaller.readManifest(new ByteArrayInputStream(
                ("test.pk3\t" + data.length + "\t" + h + "\n").getBytes("UTF-8")));
    }
    public static void main(String[] args) throws Exception {
        File dir = Files.createTempDirectory("xonotic-install-test").toFile();
        byte[] good = "test game resource bytes".getBytes("UTF-8");
        List<OfflineInstaller.Entry> entries = manifest(good);
        File target = new File(dir, "test.pk3");
        int[] opens = {0};
        OfflineInstaller.Assets assets = name -> {
            check(name.equals("game/test.pk3"), "asset path");
            opens[0]++;
            return new ByteArrayInputStream(good);
        };
        OfflineInstaller.Progress progress = (n, d, t) -> check(d <= t && d >= 0, "progress bounds");
        OfflineInstaller.install(assets, entries, dir, progress);
        check(OfflineInstaller.valid(target, entries.get(0)), "fresh installation");
        OfflineInstaller.install(assets, entries, dir, progress);
        check(opens[0] == 1, "valid existing data not recopied");
        byte[] bad = good.clone(); bad[0] ^= 1;
        Files.write(target.toPath(), bad);
        check(!OfflineInstaller.valid(target, entries.get(0)), "same-size corruption detected");
        OfflineInstaller.install(assets, entries, dir, progress);
        check(opens[0] == 2 && OfflineInstaller.valid(target, entries.get(0)), "corruption repaired");
        Files.write(target.toPath(), bad);
        try {
            OfflineInstaller.install(n -> new ByteArrayInputStream(new byte[3]), entries, dir, progress);
            throw new AssertionError("truncation accepted");
        } catch (IOException expected) { assertions++; }
        check(Arrays.equals(Files.readAllBytes(target.toPath()), bad), "failed install preserves old file");
        check(!new File(dir, "test.pk3.part").exists(), "partial removed after failure");
        try {
            OfflineInstaller.install(n -> new ByteArrayInputStream(bad), entries, dir, progress);
            throw new AssertionError("bad digest accepted");
        } catch (IOException expected) { assertions++; }
        Files.write(new File(dir, "test.pk3.part").toPath(), new byte[1]);
        OfflineInstaller.install(assets, entries, dir, progress);
        check(OfflineInstaller.valid(target, entries.get(0)), "interrupted setup retry");
        for (String invalid : new String[] {"", "../test.pk3\t1\t" + "0".repeat(64),
                "x.pk3\t-1\t" + "0".repeat(64), "x.pk3\t1\tbad"}) {
            try {
                OfflineInstaller.readManifest(new ByteArrayInputStream(invalid.getBytes("UTF-8")));
                throw new AssertionError("invalid manifest accepted");
            } catch (IOException expected) { assertions++; }
        }
        Files.delete(target.toPath()); Files.delete(dir.toPath());
        System.out.println("PASS: " + assertions + " assertions");
    }
}
