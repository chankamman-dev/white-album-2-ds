// SPDX-License-Identifier: Apache-2.0
// AYN Thor dual-screen bridge: shows a Presentation on the lower display and
// draws frames rendered by Godot into it; touches are sent back as signals.
package org.wa2.thor;

import android.app.Activity;
import android.app.Presentation;
import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.Color;
import android.hardware.display.DisplayManager;
import android.os.Build;
import android.os.Bundle;
import android.util.Log;
import android.view.Display;
import android.view.MotionEvent;
import android.view.Surface;
import android.view.SurfaceHolder;
import android.view.SurfaceView;
import android.view.View;
import android.view.Window;
import android.view.WindowInsets;
import android.view.WindowInsetsController;
import android.view.WindowManager;

import androidx.annotation.NonNull;

import org.godotengine.godot.Godot;
import org.godotengine.godot.plugin.GodotPlugin;
import org.godotengine.godot.plugin.SignalInfo;
import org.godotengine.godot.plugin.UsedByGodot;

import java.nio.ByteBuffer;
import java.util.HashSet;
import java.util.Set;

public class Wa2ThorPlugin extends GodotPlugin {
	private static final String TAG = "Wa2Thor";

	public static final int TOUCH_DOWN = 0;
	public static final int TOUCH_MOVE = 1;
	public static final int TOUCH_UP = 2;
	public static final int TOUCH_CANCEL = 3;

	private final Object frameLock = new Object();
	private BottomPresentation presentation;
	private DisplayManager.DisplayListener displayListener;
	private boolean wanted;
	private boolean paused;
	private Surface surface;
	private int surfaceWidth;
	private int surfaceHeight;
	private Bitmap bitmap;
	private Bitmap meterSprite;
	private android.graphics.Rect meterRect;
	private float meterLevel = -1f;

	public Wa2ThorPlugin(Godot godot) {
		super(godot);
	}

	@NonNull
	@Override
	public String getPluginName() {
		return "Wa2Thor";
	}

	@NonNull
	@Override
	public Set<SignalInfo> getPluginSignals() {
		Set<SignalInfo> signals = new HashSet<>();
		signals.add(new SignalInfo("bottom_surface", Integer.class, Integer.class));
		signals.add(new SignalInfo("bottom_touch", Integer.class, Float.class, Float.class));
		return signals;
	}

	/** True when a second, non-private display exists (the Thor lower screen). */
	@UsedByGodot
	public boolean hasBottomDisplay() {
		return findBottomDisplay() != null;
	}

	@UsedByGodot
	public void start() {
		wanted = true;
		runOnUiThread(this::showPresentation);
	}

	@UsedByGodot
	public void stop() {
		wanted = false;
		runOnUiThread(this::dismissPresentation);
	}

	/** Draws one RGBA8 frame on the lower screen. Called from the Godot thread. */
	@UsedByGodot
	public boolean pushFrame(byte[] rgba, int width, int height) {
		if (rgba == null || width <= 0 || height <= 0 || rgba.length < width * height * 4) {
			return false;
		}
		synchronized (frameLock) {
			Surface target = surface;
			if (target == null || !target.isValid()) {
				return false;
			}
			if (bitmap == null || bitmap.getWidth() != width || bitmap.getHeight() != height) {
				bitmap = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888);
			}
			bitmap.copyPixelsFromBuffer(ByteBuffer.wrap(rgba, 0, width * height * 4));
			Canvas canvas = null;
			try {
				canvas = target.lockCanvas(null);
				if (width == surfaceWidth && height == surfaceHeight) {
					canvas.drawBitmap(bitmap, 0, 0, null);
					drawMeter(canvas);
				} else {
					canvas.drawColor(Color.BLACK);
					canvas.drawBitmap(bitmap, null, new android.graphics.Rect(0, 0, surfaceWidth, surfaceHeight), null);
				}
			} catch (RuntimeException e) {
				Log.w(TAG, "pushFrame failed", e);
				return false;
			} finally {
				if (canvas != null) {
					try {
						target.unlockCanvasAndPost(canvas);
					} catch (RuntimeException ignored) {
					}
				}
			}
		}
		return true;
	}

	// ---- system speech (Japanese TTS for lines without a recorded voice) ----

	private android.speech.tts.TextToSpeech tts;
	/** 0 not started, 1 starting, 2 ready, -1 engine failed, -2 no Japanese voice. */
	private volatile int ttsState;

	@UsedByGodot
	public int speechState() {
		ensureTts();
		return ttsState;
	}

	@UsedByGodot
	public boolean speak(String text) {
		ensureTts();
		android.speech.tts.TextToSpeech t = tts;
		if (ttsState != 2 || t == null || text == null || text.isEmpty()) {
			return false;
		}
		t.speak(text, android.speech.tts.TextToSpeech.QUEUE_FLUSH, null, "wa2-line");
		return true;
	}

	@UsedByGodot
	public void stopSpeaking() {
		android.speech.tts.TextToSpeech t = tts;
		if (t != null) {
			t.stop();
		}
	}

	private void ensureTts() {
		if (ttsState != 0) {
			return;
		}
		Activity activity = getActivity();
		if (activity == null) {
			return;
		}
		ttsState = 1;
		Context app = activity.getApplicationContext();
		runOnUiThread(() -> {
			tts = new android.speech.tts.TextToSpeech(app, status -> {
				if (status != android.speech.tts.TextToSpeech.SUCCESS) {
					ttsState = -1;
					Log.w(TAG, "tts init failed: " + status);
					return;
				}
				tts.setAudioAttributes(new android.media.AudioAttributes.Builder()
						.setUsage(android.media.AudioAttributes.USAGE_MEDIA)
						.setContentType(android.media.AudioAttributes.CONTENT_TYPE_SPEECH)
						.build());
				int r = tts.setLanguage(java.util.Locale.JAPAN);
				boolean ok = r != android.speech.tts.TextToSpeech.LANG_MISSING_DATA
						&& r != android.speech.tts.TextToSpeech.LANG_NOT_SUPPORTED;
				ttsState = ok ? 2 : -2;
				Log.i(TAG, "tts engine " + tts.getDefaultEngine() + " japanese=" + r);
			});
		});
	}

	// ---- word lookup (offline dictionary: JMdict + zh.wiktionary, SQLite) -------

	private static final String DICT_ASSET = "addons/thor_dualscreen/dict/thor_dict.db";
	private volatile android.database.sqlite.SQLiteDatabase dict;
	private volatile boolean dictLoading;

	/** True once the dictionary is open; the first call copies it out of the APK in the background. */
	@UsedByGodot
	public boolean dictReady() {
		if (dict == null && !dictLoading) {
			Activity activity = getActivity();
			if (activity == null) {
				return false;
			}
			dictLoading = true;
			Context app = activity.getApplicationContext();
			Thread t = new Thread(() -> {
				try {
					java.io.File file = new java.io.File(app.getFilesDir(), "thor_dict.db");
					java.io.File stamp = new java.io.File(app.getFilesDir(), "thor_dict.stamp");
					long apkTime = app.getPackageManager().getPackageInfo(app.getPackageName(), 0).lastUpdateTime;
					String want = String.valueOf(apkTime);
					String have = stamp.exists() ? new String(java.nio.file.Files.readAllBytes(stamp.toPath())) : "";
					if (!file.exists() || !want.equals(have)) {
						try (java.io.InputStream in = app.getAssets().open(DICT_ASSET);
							 java.io.OutputStream out = new java.io.FileOutputStream(file)) {
							byte[] buf = new byte[1 << 16];
							int n;
							while ((n = in.read(buf)) > 0) {
								out.write(buf, 0, n);
							}
						}
						java.nio.file.Files.write(stamp.toPath(), want.getBytes());
					}
					dict = android.database.sqlite.SQLiteDatabase.openDatabase(file.getPath(), null,
							android.database.sqlite.SQLiteDatabase.OPEN_READONLY);
					Log.i(TAG, "dictionary ready");
				} catch (Throwable e) {
					Log.w(TAG, "dictionary failed", e);
				}
			}, "wa2-dict");
			t.setPriority(Thread.MIN_PRIORITY);
			t.start();
		}
		return dict != null;
	}

	/**
	 * Up to three entries for a word as JSON: [{"h":head,"r":reading,"p":pos,"zh":..,"en":..}].
	 * Entries whose reading matches the token's reading come first.
	 */
	@UsedByGodot
	public String lookup(String surface, String base, String readingHira) {
		android.database.sqlite.SQLiteDatabase db = dict;
		org.json.JSONArray result = new org.json.JSONArray();
		if (db == null) {
			return result.toString();
		}
		java.util.LinkedHashMap<Long, org.json.JSONObject> found = new java.util.LinkedHashMap<>();
		for (String key : new String[] {base, surface}) {
			if (key == null || key.isEmpty() || key.equals("*")) {
				continue;
			}
			try (android.database.Cursor c = db.rawQuery(
					"SELECT e.id, e.head, e.reading, e.pos, e.zh, e.en FROM key k JOIN entry e ON e.id = k.id WHERE k.k = ? LIMIT 12",
					new String[] {key})) {
				while (c.moveToNext()) {
					long id = c.getLong(0);
					if (found.containsKey(id)) {
						continue;
					}
					org.json.JSONObject o = new org.json.JSONObject();
					o.put("h", c.getString(1));
					o.put("r", c.getString(2));
					o.put("p", c.getString(3));
					o.put("zh", c.getString(4));
					o.put("en", c.getString(5));
					found.put(id, o);
				}
			} catch (Exception e) {
				Log.w(TAG, "lookup failed", e);
			}
			if (!found.isEmpty()) {
				break;
			}
		}
		java.util.List<org.json.JSONObject> list = new java.util.ArrayList<>(found.values());
		list.sort((a, b) -> Boolean.compare(!readingHira.equals(a.optString("r")), !readingHira.equals(b.optString("r"))));
		for (int i = 0; i < Math.min(3, list.size()); i++) {
			result.put(list.get(i));
		}
		return result.toString();
	}

	/** Sentence breakdown as JSON: [{"s":surface,"r":reading,"b":base form,"p":part of speech}]. */
	@UsedByGodot
	public String analyze(String text) {
		com.atilika.kuromoji.ipadic.Tokenizer t = tokenizer;
		org.json.JSONArray result = new org.json.JSONArray();
		if (t == null || text == null) {
			return result.toString();
		}
		try {
			for (com.atilika.kuromoji.ipadic.Token token : t.tokenize(text)) {
				org.json.JSONObject o = new org.json.JSONObject();
				o.put("s", token.getSurface());
				o.put("r", token.getReading() == null ? "*" : token.getReading());
				o.put("b", token.getBaseForm() == null ? "*" : token.getBaseForm());
				o.put("p", token.getPartOfSpeechLevel1());
				result.put(o);
			}
		} catch (Exception e) {
			Log.w(TAG, "analyze failed", e);
		}
		return result.toString();
	}

	// ---- AnkiDroid (FlashCardsContract content provider) ------------------------

	private static final String ANKI_PACKAGE = "com.ichi2.anki";
	private static final String ANKI_PERMISSION = "com.ichi2.anki.permission.READ_WRITE_DATABASE";
	private static final String ANKI_AUTHORITY = "content://com.ichi2.anki.flashcards";
	private static final String ANKI_DECK = "WHITE ALBUM2";
	private static final String ANKI_MODEL = "WHITE ALBUM 2 DS 生词";
	private static final String[] ANKI_FIELDS = {"Word", "Reading", "Meaning", "Sentence", "Source"};
	private static final String ANKI_CSS = ".card{font-family:sans-serif;font-size:22px;text-align:center;color:#223;background:#f4f8fb}"
			+ ".w{font-size:44px;margin:12px}.r{font-size:26px;color:#2a7f9e}.m{margin:12px;font-size:22px}"
			+ ".s{font-size:24px;margin-top:16px}.src{font-size:14px;color:#889;margin-top:12px}";
	private static final String ANKI_FRONT = "<div class=w>{{Word}}</div>";
	private static final String ANKI_BACK = "{{FrontSide}}<hr id=answer><div class=r>{{Reading}}</div>"
			+ "<div class=m>{{Meaning}}</div><div class=s>{{furigana:Sentence}}</div><div class=src>{{Source}}</div>";

	/**
	 * Adds a note to AnkiDroid. Returns "ok", "duplicate", "no_anki", "permission" (the system dialog
	 * has been shown; try again after allowing) or "error:...".
	 */
	@UsedByGodot
	public String ankiAdd(String word, String reading, String meaning, String sentence, String source) {
		Activity activity = getActivity();
		if (activity == null) {
			return "error:no activity";
		}
		try {
			activity.getPackageManager().getPackageInfo(ANKI_PACKAGE, 0);
		} catch (Exception e) {
			return "no_anki";
		}
		if (activity.checkSelfPermission(ANKI_PERMISSION) != android.content.pm.PackageManager.PERMISSION_GRANTED) {
			activity.runOnUiThread(() -> activity.requestPermissions(new String[] {ANKI_PERMISSION}, 7702));
			return "permission";
		}
		try {
			android.content.ContentResolver cr = activity.getContentResolver();
			long deck = ankiDeck(cr);
			long model = ankiModel(cr, deck);
			try (android.database.Cursor c = cr.query(android.net.Uri.parse(ANKI_AUTHORITY + "/notes"), new String[] {"_id"},
					"mid:" + model + " \"Word:" + word.replace("\"", "") + "\"", null, null)) {
				if (c != null && c.getCount() > 0) {
					return "duplicate";
				}
			}
			android.content.ContentValues v = new android.content.ContentValues();
			v.put("mid", model);
			v.put("flds", String.join("\u001f", word, reading, meaning, sentence, source));
			v.put("tags", "WA2");
			android.net.Uri note = cr.insert(android.net.Uri.parse(ANKI_AUTHORITY + "/notes"), v);
			if (note == null) {
				return "error:insert failed";
			}
			// make sure the card lands in our deck
			android.content.ContentValues dv = new android.content.ContentValues();
			dv.put("deck_id", deck);
			cr.update(note.buildUpon().appendPath("cards").appendPath("0").build(), dv, null, null);
			return "ok";
		} catch (Exception e) {
			Log.w(TAG, "anki add failed", e);
			return "error:" + e.getMessage();
		}
	}

	private long ankiDeck(android.content.ContentResolver cr) {
		android.net.Uri decks = android.net.Uri.parse(ANKI_AUTHORITY + "/decks");
		try (android.database.Cursor c = cr.query(decks, new String[] {"deck_id", "deck_name"}, null, null, null)) {
			while (c != null && c.moveToNext()) {
				if (ANKI_DECK.equals(c.getString(1))) {
					return c.getLong(0);
				}
			}
		}
		android.content.ContentValues v = new android.content.ContentValues();
		v.put("deck_name", ANKI_DECK);
		return Long.parseLong(cr.insert(decks, v).getLastPathSegment());
	}

	private long ankiModel(android.content.ContentResolver cr, long deck) {
		android.net.Uri models = android.net.Uri.parse(ANKI_AUTHORITY + "/models");
		try (android.database.Cursor c = cr.query(models, new String[] {"_id", "name"}, null, null, null)) {
			while (c != null && c.moveToNext()) {
				if (ANKI_MODEL.equals(c.getString(1))) {
					return c.getLong(0);
				}
			}
		}
		android.content.ContentValues v = new android.content.ContentValues();
		v.put("name", ANKI_MODEL);
		v.put("field_names", String.join("\u001f", ANKI_FIELDS));
		v.put("num_cards", 1);
		v.put("css", ANKI_CSS);
		v.put("deck_id", deck);
		v.put("sort_field_index", 0);
		v.put("type", 0);
		android.net.Uri model = cr.insert(models, v);
		android.content.ContentValues t = new android.content.ContentValues();
		t.put("card_template_name", "Card 1");
		t.put("question_format", ANKI_FRONT);
		t.put("answer_format", ANKI_BACK);
		t.put("browser_question_format", "");
		t.put("browser_answer_format", "");
		cr.update(model.buildUpon().appendPath("templates").appendPath("0").build(), t, null, null);
		return Long.parseLong(model.getLastPathSegment());
	}

	// ---- furigana ------------------------------------------------------------

	private volatile com.atilika.kuromoji.ipadic.Tokenizer tokenizer;
	private volatile boolean tokenizerLoading;

	/** True once the dictionary is loaded; the first call starts loading it in the background. */
	@UsedByGodot
	public boolean furiganaReady() {
		if (tokenizer == null && !tokenizerLoading) {
			tokenizerLoading = true;
			Thread loader = new Thread(() -> {
				try {
					long t0 = System.currentTimeMillis();
					tokenizer = new com.atilika.kuromoji.ipadic.Tokenizer();
					Log.i(TAG, "kuromoji ready in " + (System.currentTimeMillis() - t0) + " ms");
				} catch (Throwable e) {
					Log.w(TAG, "kuromoji failed", e);
				}
			}, "wa2-kuromoji");
			loader.setPriority(Thread.MIN_PRIORITY);
			loader.start();
		}
		return tokenizer != null;
	}

	/** One "surface TAB katakana-reading" line per token, or "" when the dictionary is not ready. */
	@UsedByGodot
	public String readings(String text) {
		com.atilika.kuromoji.ipadic.Tokenizer t = tokenizer;
		if (t == null || text == null || text.isEmpty()) {
			return "";
		}
		StringBuilder sb = new StringBuilder();
		for (com.atilika.kuromoji.ipadic.Token token : t.tokenize(text)) {
			String reading = token.getReading();
			sb.append(token.getSurface()).append('\t').append(reading == null ? "*" : reading).append('\n');
		}
		return sb.toString();
	}

	/** BGM meter sprite (RGBA8) and where it sits in the frame. */
	@UsedByGodot
	public void setMeterSprite(byte[] rgba, int width, int height, int x, int y) {
		if (rgba == null || width <= 0 || height <= 0 || rgba.length < width * height * 4) {
			return;
		}
		Bitmap sprite = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888);
		sprite.copyPixelsFromBuffer(ByteBuffer.wrap(rgba, 0, width * height * 4));
		synchronized (frameLock) {
			meterSprite = sprite;
			meterRect = new android.graphics.Rect(x, y, x + width, y + height);
		}
	}

	/**
	 * Live meter level 0..1 (negative hides it). Only the meter strip is redrawn,
	 * from the last full frame, so the rest of the lower screen is untouched.
	 */
	@UsedByGodot
	public void setMeterLevel(float level) {
		synchronized (frameLock) {
			meterLevel = level;
			Surface target = surface;
			if (target == null || !target.isValid() || bitmap == null || meterRect == null
					|| bitmap.getWidth() != surfaceWidth || bitmap.getHeight() != surfaceHeight) {
				return;
			}
			android.graphics.Rect dirty = new android.graphics.Rect(meterRect);
			Canvas canvas = null;
			try {
				canvas = target.lockCanvas(dirty);
				// the surface may hand back a larger dirty area: repaint all of it from the frame
				canvas.drawBitmap(bitmap, dirty, dirty, null);
				drawMeter(canvas);
			} catch (RuntimeException e) {
				Log.w(TAG, "meter draw failed", e);
			} finally {
				if (canvas != null) {
					try {
						target.unlockCanvasAndPost(canvas);
					} catch (RuntimeException ignored) {
					}
				}
			}
		}
	}

	private void drawMeter(Canvas canvas) {
		if (meterSprite == null || meterRect == null || meterLevel <= 0f) {
			return;
		}
		int w = Math.round(meterSprite.getWidth() * Math.min(1f, meterLevel));
		if (w <= 0) {
			return;
		}
		android.graphics.Rect src = new android.graphics.Rect(0, 0, w, meterSprite.getHeight());
		android.graphics.Rect dst = new android.graphics.Rect(meterRect.left, meterRect.top, meterRect.left + w, meterRect.bottom);
		canvas.drawBitmap(meterSprite, src, dst, null);
	}

	@Override
	public void onMainPause() {
		paused = true;
		dismissPresentation();
		stopSpeaking();
	}

	@Override
	public void onMainResume() {
		paused = false;
		if (wanted) {
			showPresentation();
		}
	}

	@Override
	public void onMainDestroy() {
		wanted = false;
		dismissPresentation();
		if (tts != null) {
			tts.shutdown();
			tts = null;
		}
		Activity activity = getActivity();
		if (activity != null && displayListener != null) {
			DisplayManager dm = (DisplayManager) activity.getSystemService(Context.DISPLAY_SERVICE);
			dm.unregisterDisplayListener(displayListener);
			displayListener = null;
		}
	}

	private Display findBottomDisplay() {
		Activity activity = getActivity();
		if (activity == null) {
			return null;
		}
		DisplayManager dm = (DisplayManager) activity.getSystemService(Context.DISPLAY_SERVICE);
		Display own = activity.getWindowManager().getDefaultDisplay();
		int ownId = own != null ? own.getDisplayId() : Display.DEFAULT_DISPLAY;
		Display fallback = null;
		for (Display display : dm.getDisplays(DisplayManager.DISPLAY_CATEGORY_PRESENTATION)) {
			if (display.getDisplayId() == ownId || (display.getFlags() & Display.FLAG_PRIVATE) != 0) {
				continue;
			}
			if (display.getState() != Display.STATE_OFF || fallback == null) {
				fallback = display;
			}
		}
		return fallback;
	}

	private void ensureDisplayListener(Activity activity) {
		if (displayListener != null) {
			return;
		}
		displayListener = new DisplayManager.DisplayListener() {
			@Override
			public void onDisplayAdded(int displayId) {
				if (wanted && !paused && presentation == null) {
					showPresentation();
				}
			}

			@Override
			public void onDisplayRemoved(int displayId) {
				if (presentation != null && presentation.getDisplay().getDisplayId() == displayId) {
					dismissPresentation();
				}
			}

			@Override
			public void onDisplayChanged(int displayId) {
			}
		};
		DisplayManager dm = (DisplayManager) activity.getSystemService(Context.DISPLAY_SERVICE);
		dm.registerDisplayListener(displayListener, null);
	}

	private void showPresentation() {
		Activity activity = getActivity();
		if (activity == null || paused || !wanted || presentation != null) {
			return;
		}
		ensureDisplayListener(activity);
		Display display = findBottomDisplay();
		if (display == null) {
			Log.i(TAG, "no bottom display");
			return;
		}
		try {
			presentation = new BottomPresentation(activity, display);
			presentation.setOnDismissListener(d -> {
				if (presentation == d) {
					presentation = null;
				}
			});
			presentation.show();
			Log.i(TAG, "presentation on display " + display.getDisplayId());
		} catch (WindowManager.InvalidDisplayException e) {
			Log.w(TAG, "cannot show presentation", e);
			presentation = null;
		}
	}

	private void dismissPresentation() {
		if (presentation != null) {
			BottomPresentation p = presentation;
			presentation = null;
			p.dismiss();
		}
	}

	private void setSurface(Surface s, int width, int height) {
		synchronized (frameLock) {
			surface = s;
			surfaceWidth = width;
			surfaceHeight = height;
		}
		runOnRenderThread(() -> emitSignal("bottom_surface", width, height));
	}

	private void sendTouch(int action, float x, float y) {
		runOnRenderThread(() -> emitSignal("bottom_touch", action, x, y));
	}

	private final class BottomPresentation extends Presentation implements SurfaceHolder.Callback {
		BottomPresentation(Context outer, Display display) {
			super(outer, display);
		}

		@Override
		protected void onCreate(Bundle savedInstanceState) {
			super.onCreate(savedInstanceState);
			Window window = getWindow();
			if (window != null) {
				// Never take key focus from the game: the gamepad must keep driving the top screen.
				window.addFlags(WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE
						| WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
				window.setBackgroundDrawable(new android.graphics.drawable.ColorDrawable(Color.BLACK));
			}
			SurfaceView view = new SurfaceView(getContext());
			view.getHolder().addCallback(this);
			view.setOnTouchListener(this::onTouch);
			setContentView(view);
			if (window != null && Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
				WindowInsetsController controller = window.getInsetsController();
				if (controller != null) {
					controller.hide(WindowInsets.Type.systemBars());
					controller.setSystemBarsBehavior(WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE);
				}
			}
		}

		private boolean onTouch(View v, MotionEvent event) {
			switch (event.getActionMasked()) {
				case MotionEvent.ACTION_DOWN:
					sendTouch(TOUCH_DOWN, event.getX(0), event.getY(0));
					break;
				case MotionEvent.ACTION_MOVE:
					sendTouch(TOUCH_MOVE, event.getX(0), event.getY(0));
					break;
				case MotionEvent.ACTION_UP:
					sendTouch(TOUCH_UP, event.getX(0), event.getY(0));
					break;
				case MotionEvent.ACTION_CANCEL:
					sendTouch(TOUCH_CANCEL, event.getX(0), event.getY(0));
					break;
				default:
					break;
			}
			return true;
		}

		@Override
		public void surfaceCreated(@NonNull SurfaceHolder holder) {
		}

		@Override
		public void surfaceChanged(@NonNull SurfaceHolder holder, int format, int width, int height) {
			setSurface(holder.getSurface(), width, height);
		}

		@Override
		public void surfaceDestroyed(@NonNull SurfaceHolder holder) {
			setSurface(null, 0, 0);
		}
	}
}
