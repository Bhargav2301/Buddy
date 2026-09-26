package app.buddy

import android.app.*
import android.content.Context
import android.content.Intent
import android.graphics.Color
import android.graphics.PixelFormat
import android.graphics.drawable.GradientDrawable
import android.os.Build
import android.os.IBinder
import android.provider.Settings
import android.service.quicksettings.TileService
import android.view.Gravity
import android.view.MotionEvent
import android.view.View
import android.view.WindowManager
import android.widget.TextView
import kotlin.math.abs

class BuddyBubbleService : Service() {
    private var bubble: View? = null
    private val manager by lazy { getSystemService(WindowManager::class.java) }
    override fun onBind(intent: Intent?): IBinder? = null
    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == "STOP" || !Settings.canDrawOverlays(this)) { stopSelf(); return START_NOT_STICKY }
        val notifications = getSystemService(NotificationManager::class.java)
        notifications.createNotificationChannel(NotificationChannel("buddy_bubble", "Buddy floating shortcut", NotificationManager.IMPORTANCE_LOW))
        val open = PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        val stop = PendingIntent.getService(this, 1, Intent(this, BuddyBubbleService::class.java).setAction("STOP"), PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        startForeground(11, Notification.Builder(this, "buddy_bubble").setSmallIcon(R.drawable.ic_buddy).setContentTitle("Buddy is one tap away").setContentText("Shortcut only · no recording or screen capture").setContentIntent(open).addAction(Notification.Action.Builder(null, "Hide bubble", stop).build()).setOngoing(true).build())
        if (bubble == null) {
            val size = (56 * resources.displayMetrics.density).toInt()
            val view = TextView(this).apply { text = "B"; textSize = 23f; gravity = Gravity.CENTER; setTextColor(Color.rgb(17, 24, 32)); contentDescription = "Open Buddy. Drag to move."; background = GradientDrawable().apply { shape = GradientDrawable.OVAL; setColor(Color.rgb(142, 228, 197)) } }
            val params = WindowManager.LayoutParams(size, size, WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY, WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE, PixelFormat.TRANSLUCENT).apply { gravity = Gravity.TOP or Gravity.START; x = 20; y = 240 }
            var startX = 0; var startY = 0; var touchX = 0f; var touchY = 0f
            view.setOnClickListener { startActivity(Intent(this, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP)) }
            view.setOnTouchListener { v, event ->
                when (event.action) {
                    MotionEvent.ACTION_DOWN -> { startX = params.x; startY = params.y; touchX = event.rawX; touchY = event.rawY; true }
                    MotionEvent.ACTION_MOVE -> { params.x = (startX + event.rawX - touchX).toInt().coerceIn(0, (resources.displayMetrics.widthPixels - size).coerceAtLeast(0)); params.y = (startY + event.rawY - touchY).toInt().coerceIn(0, (resources.displayMetrics.heightPixels - size).coerceAtLeast(0)); manager.updateViewLayout(view, params); true }
                    MotionEvent.ACTION_UP -> { if (abs(event.rawX - touchX) + abs(event.rawY - touchY) < 14) v.performClick(); true }
                    else -> false
                }
            }
            try { manager.addView(view, params); bubble = view } catch (_: Exception) { stopSelf() }
        }
        return START_NOT_STICKY
    }
    override fun onDestroy() { bubble?.let { runCatching { manager.removeView(it) } }; bubble = null; super.onDestroy() }
}

class BuddyTileService : TileService() {
    override fun onClick() {
        super.onClick()
        val intent = Intent(this, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        val launch = PendingIntent.getActivity(this, 9, intent, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        if (Build.VERSION.SDK_INT >= 34) startActivityAndCollapse(launch)
        else launch.send()
    }
}
