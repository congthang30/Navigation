package com.vps.navigation.tts;

import android.content.Context;
import android.content.pm.PackageManager;
import android.media.AudioAttributes;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.speech.tts.TextToSpeech;
import android.speech.tts.UtteranceProgressListener;
import com.unity3d.player.UnityPlayer;
import java.util.Locale;
import java.util.UUID;

/**
 * Lightweight Android bridge binding Unity to Google Text-to-Speech services
 * (preferring com.google.android.tts with clean fallback to system TTS).
 */
public final class VpsGoogleTtsBridge {
    private static final String GOOGLE_TTS_PACKAGE = "com.google.android.tts";

    private static final Handler MAIN_HANDLER = new Handler(Looper.getMainLooper());
    private static final Object LOCK = new Object();

    private static Context appContext;
    private static TextToSpeech tts;
    private static boolean initializing;
    private static boolean ready;
    private static boolean speaking;
    private static boolean usingGoogleEngine;
    private static String activeEngine = "";
    private static String languageTag = "";
    private static String statusSummary = "Chưa khởi tạo";
    private static String lastError = "";

    private static String pendingText;
    private static float pendingRate = 1.0f;
    private static float pendingPitch = 1.0f;

    private VpsGoogleTtsBridge() {
    }

    public static void init() {
        Context context = resolveUnityContext();
        if (context != null) {
            initWithContext(context);
        } else {
            synchronized (LOCK) {
                lastError = "Không tìm thấy Android Context từ UnityPlayer.";
                statusSummary = "Thiếu Context";
            }
        }
    }

    public static void initWithContext(final Context context) {
        if (context == null) {
            return;
        }
        final Context resolvedApp = context.getApplicationContext() != null
                ? context.getApplicationContext()
                : context;

        synchronized (LOCK) {
            appContext = resolvedApp;
            if (ready && tts != null) {
                return;
            }
            if (initializing) {
                return;
            }
            initializing = true;
            lastError = "";
            statusSummary = "Đang kết nối Google TTS...";
        }

        MAIN_HANDLER.post(new Runnable() {
            @Override
            public void run() {
                boolean hasGoogleTts = isPackageInstalled(resolvedApp, GOOGLE_TTS_PACKAGE);
                createTtsInstance(resolvedApp, hasGoogleTts, hasGoogleTts);
            }
        });
    }

    public static void speak(final String text, final float speechRate, final float pitch) {
        if (text == null || text.trim().isEmpty()) {
            return;
        }

        final String trimmed = text.trim();
        final float clampedRate = clamp(speechRate, 0.5f, 2.0f);
        final float clampedPitch = clamp(pitch, 0.5f, 2.0f);

        Context ctx;
        boolean shouldInit = false;
        synchronized (LOCK) {
            ctx = appContext;
            if (ctx == null) {
                ctx = resolveUnityContext();
                if (ctx != null && ctx.getApplicationContext() != null) {
                    ctx = ctx.getApplicationContext();
                }
                appContext = ctx;
            }
            if (!ready || tts == null) {
                pendingText = trimmed;
                pendingRate = clampedRate;
                pendingPitch = clampedPitch;
                shouldInit = !initializing && ctx != null;
                if (ctx == null) {
                    lastError = "Thiếu Android Context để phát TTS.";
                }
            }
        }

        if (shouldInit) {
            initWithContext(ctx);
            return;
        }

        MAIN_HANDLER.post(new Runnable() {
            @Override
            public void run() {
                synchronized (LOCK) {
                    if (!ready || tts == null) {
                        pendingText = trimmed;
                        pendingRate = clampedRate;
                        pendingPitch = clampedPitch;
                        return;
                    }
                    speakLocked(trimmed, clampedRate, clampedPitch);
                }
            }
        });
    }

    public static void stop() {
        synchronized (LOCK) {
            pendingText = null;
            speaking = false;
        }
        MAIN_HANDLER.post(new Runnable() {
            @Override
            public void run() {
                synchronized (LOCK) {
                    if (tts != null) {
                        try {
                            tts.stop();
                        } catch (Throwable ignored) {
                        }
                    }
                    speaking = false;
                }
            }
        });
    }

    public static void shutdown() {
        MAIN_HANDLER.post(new Runnable() {
            @Override
            public void run() {
                synchronized (LOCK) {
                    pendingText = null;
                    speaking = false;
                    ready = false;
                    initializing = false;
                    if (tts != null) {
                        try {
                            tts.stop();
                            tts.shutdown();
                        } catch (Throwable ignored) {
                        }
                        tts = null;
                    }
                }
            }
        });
    }

    public static boolean isReady() {
        synchronized (LOCK) {
            return ready;
        }
    }

    public static boolean isInitializing() {
        synchronized (LOCK) {
            return initializing;
        }
    }

    public static boolean isSpeaking() {
        synchronized (LOCK) {
            if (speaking) {
                return true;
            }
            if (tts != null && ready) {
                try {
                    return tts.isSpeaking();
                } catch (Throwable ignored) {
                }
            }
            return false;
        }
    }

    public static boolean isUsingGoogleEngine() {
        synchronized (LOCK) {
            return usingGoogleEngine;
        }
    }

    public static String getActiveEngine() {
        synchronized (LOCK) {
            return activeEngine != null ? activeEngine : "";
        }
    }

    public static String getLanguageTag() {
        synchronized (LOCK) {
            return languageTag != null ? languageTag : "";
        }
    }

    public static String getStatusSummary() {
        synchronized (LOCK) {
            return statusSummary != null ? statusSummary : "";
        }
    }

    public static String getLastError() {
        synchronized (LOCK) {
            return lastError != null ? lastError : "";
        }
    }

    private static void createTtsInstance(
            final Context context,
            final boolean preferGoogleEngine,
            final boolean allowFallbackToSystem) {
        TextToSpeech.OnInitListener listener = new TextToSpeech.OnInitListener() {
            @Override
            public void onInit(int status) {
                if (status != TextToSpeech.SUCCESS) {
                    synchronized (LOCK) {
                        if (tts != null) {
                            try {
                                tts.shutdown();
                            } catch (Throwable ignored) {
                            }
                            tts = null;
                        }
                    }
                    if (preferGoogleEngine && allowFallbackToSystem) {
                        createTtsInstance(context, false, false);
                        return;
                    }
                    synchronized (LOCK) {
                        initializing = false;
                        ready = false;
                        usingGoogleEngine = false;
                        lastError = "TextToSpeech khởi tạo thất bại (status=" + status + ")";
                        statusSummary = "Lỗi TTS (" + status + ")";
                    }
                    return;
                }

                synchronized (LOCK) {
                    if (tts == null) {
                        initializing = false;
                        ready = false;
                        return;
                    }
                    configureTtsLocked(tts, preferGoogleEngine);
                    ready = true;
                    initializing = false;
                    lastError = "";
                    if (pendingText != null && !pendingText.isEmpty()) {
                        String textToSpeak = pendingText;
                        float r = pendingRate;
                        float p = pendingPitch;
                        pendingText = null;
                        speakLocked(textToSpeak, r, p);
                    }
                }
            }
        };

        synchronized (LOCK) {
            try {
                if (preferGoogleEngine) {
                    tts = new TextToSpeech(context, listener, GOOGLE_TTS_PACKAGE);
                } else {
                    tts = new TextToSpeech(context, listener);
                }
            } catch (Throwable t) {
                tts = null;
                if (preferGoogleEngine && allowFallbackToSystem) {
                    // Retry with system default engine on next turn
                    MAIN_HANDLER.post(new Runnable() {
                        @Override
                        public void run() {
                            createTtsInstance(context, false, false);
                        }
                    });
                    return;
                }
                initializing = false;
                ready = false;
                lastError = t.getMessage() != null ? t.getMessage() : t.getClass().getSimpleName();
                statusSummary = "Không thể mở dịch vụ TTS";
            }
        }
    }

    private static void configureTtsLocked(TextToSpeech instance, boolean requestedGoogleEngine) {
        try {
            AudioAttributes attrs = new AudioAttributes.Builder()
                    .setUsage(AudioAttributes.USAGE_MEDIA)
                    .setContentType(AudioAttributes.CONTENT_TYPE_SPEECH)
                    .build();
            instance.setAudioAttributes(attrs);
        } catch (Throwable ignored) {
        }

        instance.setOnUtteranceProgressListener(new UtteranceProgressListener() {
            @Override
            public void onStart(String utteranceId) {
                synchronized (LOCK) {
                    speaking = true;
                }
            }

            @Override
            public void onDone(String utteranceId) {
                synchronized (LOCK) {
                    speaking = false;
                }
            }

            @Override
            public void onError(String utteranceId) {
                synchronized (LOCK) {
                    speaking = false;
                    lastError = "Lỗi phát giọng nói TTS.";
                }
            }

            @Override
            public void onError(String utteranceId, int errorCode) {
                synchronized (LOCK) {
                    speaking = false;
                    lastError = "Lỗi phát giọng nói TTS (mã " + errorCode + ").";
                }
            }
        });

        Locale viVn = new Locale("vi", "VN");
        Locale viOnly = new Locale("vi");
        String resolvedLang;
        int viSupport = safeSetLanguage(instance, viVn);
        if (viSupport >= TextToSpeech.LANG_AVAILABLE) {
            resolvedLang = "vi-VN";
        } else {
            int viBaseSupport = safeSetLanguage(instance, viOnly);
            if (viBaseSupport >= TextToSpeech.LANG_AVAILABLE) {
                resolvedLang = "vi";
            } else {
                Locale def = Locale.getDefault();
                if (def != null && safeSetLanguage(instance, def) >= TextToSpeech.LANG_AVAILABLE) {
                    resolvedLang = def.toLanguageTag();
                } else {
                    safeSetLanguage(instance, Locale.US);
                    resolvedLang = "en-US";
                }
                lastError = "Thiết bị chưa tải gói giọng tiếng Việt (mã " + viSupport + "), tạm dùng " + resolvedLang + ".";
            }
        }

        String defaultEngine = "";
        try {
            defaultEngine = instance.getDefaultEngine();
        } catch (Throwable ignored) {
        }

        usingGoogleEngine = requestedGoogleEngine || GOOGLE_TTS_PACKAGE.equals(defaultEngine);
        activeEngine = requestedGoogleEngine ? GOOGLE_TTS_PACKAGE : (defaultEngine != null ? defaultEngine : "system");
        languageTag = resolvedLang;
        statusSummary = (usingGoogleEngine ? "Google Android TTS" : "Android TTS") + " (" + resolvedLang + ")";
    }

    private static int safeSetLanguage(TextToSpeech instance, Locale locale) {
        try {
            return instance.setLanguage(locale);
        } catch (Throwable t) {
            return TextToSpeech.LANG_NOT_SUPPORTED;
        }
    }

    private static void speakLocked(String text, float rate, float pitch) {
        if (tts == null || !ready) {
            return;
        }
        try {
            tts.setSpeechRate(rate);
            tts.setPitch(pitch);
            String utteranceId = UUID.randomUUID().toString();
            Bundle params = new Bundle();
            params.putString(TextToSpeech.Engine.KEY_PARAM_UTTERANCE_ID, utteranceId);
            speaking = true;
            int code = tts.speak(text, TextToSpeech.QUEUE_FLUSH, params, utteranceId);
            if (code != TextToSpeech.SUCCESS) {
                speaking = false;
                lastError = "TextToSpeech.speak trả về lỗi (" + code + ").";
            }
        } catch (Throwable t) {
            speaking = false;
            lastError = t.getMessage() != null ? t.getMessage() : t.getClass().getSimpleName();
        }
    }

    private static Context resolveUnityContext() {
        try {
            if (UnityPlayer.currentContext != null) {
                return UnityPlayer.currentContext;
            }
        } catch (Throwable ignored) {
        }
        try {
            return UnityPlayer.currentActivity;
        } catch (Throwable ignored) {
            return null;
        }
    }

    private static boolean isPackageInstalled(Context context, String packageName) {
        if (context == null || packageName == null) {
            return false;
        }
        try {
            PackageManager pm = context.getPackageManager();
            if (pm == null) {
                return false;
            }
            pm.getPackageInfo(packageName, 0);
            return true;
        } catch (Throwable ignored) {
            return false;
        }
    }

    private static float clamp(float value, float min, float max) {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
