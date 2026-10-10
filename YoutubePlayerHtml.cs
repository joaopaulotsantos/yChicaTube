using System.Text.RegularExpressions;

namespace yChicaTube;

internal static class YoutubePlayerHtml
{
    private static readonly Regex IdPattern = new(
        @"(?:v=([A-Za-z0-9_-]{11}))|(?:youtu\.be/([A-Za-z0-9_-]{11}))|(?:/(?:embed|shorts|v|watch)/([A-Za-z0-9_-]{11}))", 
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public const string AppOrigin = "https://www.youtube-nocookie.com";

    public static bool TryGetVideoId(string? url, out string id)
    {
        id = string.Empty;
        if (string.IsNullOrWhiteSpace(url)) return false;

        var trimmed = url.Trim();
        if (Regex.IsMatch(trimmed, @"^[A-Za-z0-9_-]{11}$"))
        {
            id = trimmed;
            return true;
        }

        var decodedUrl = System.Net.WebUtility.UrlDecode(trimmed);
        var match = IdPattern.Match(decodedUrl);
        if (match.Success)
        {
            for (int i = 1; i < match.Groups.Count; i++)
            {
                if (!string.IsNullOrEmpty(match.Groups[i].Value))
                {
                    id = match.Groups[i].Value;
                    return true;
                }
            }
        }
        return false;
    }

    public static string Build(IEnumerable<string> ids, string selectedId)
    {
        var safeList = new List<string>();
        foreach (var rawId in ids ?? Enumerable.Empty<string>())
        {
            if (TryGetVideoId(rawId, out var parsedId) && !safeList.Contains(parsedId))
            {
                safeList.Add(parsedId);
            }
        }

        string cleanSelectedId = string.Empty;
        if (!string.IsNullOrWhiteSpace(selectedId) && TryGetVideoId(selectedId, out var selId))
        {
            cleanSelectedId = selId;
            if (!safeList.Contains(selId))
            {
                safeList.Insert(0, selId);
            }
        }

        if (safeList.Count == 0)
        {
            safeList.Add("M7lc1UVf-VE");
        }

        var safeIds = safeList.ToArray();
        var initial = string.IsNullOrEmpty(cleanSelectedId) ? 0 : Array.IndexOf(safeIds, cleanSelectedId);
        if (initial < 0) initial = 0;
        var list = string.Join(",", safeIds.Select(id => $"'{id}'"));
        var storiesEndedText = Localization.AppStrings.Get("StoriesEndedHtml");
        var nextMealText = Localization.AppStrings.Get("SeeYouNextMealHtml");
        return """
<!doctype html>
<html>
<head>
<meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1,user-scalable=no">
<meta name="referrer" content="strict-origin-when-cross-origin">
<style>
html,body{margin:0;padding:0;width:100%;height:100%;background:#000;overflow:hidden;user-select:none;-webkit-user-select:none;-webkit-tap-highlight-color:transparent}
#player-wrapper{position:absolute;top:0;left:0;width:100%;height:100%;overflow:hidden;background:#000;z-index:1}
#player{position:absolute;top:0;left:0;width:100%;height:100%;border:none}
iframe{position:absolute !important;top:0 !important;left:0 !important;width:100% !important;height:100% !important;border:none !important}
#touch-shield{position:fixed;top:0;left:0;right:0;bottom:0;width:100%;height:100%;z-index:99999;background:transparent;cursor:pointer;-webkit-tap-highlight-color:transparent}
.ytp-caption-window-bottom,
.caption-window,
.ytp-subtitles-player,
.ytp-caption-segment,
.ytp-subtitles,
.caption-visual-line,
.ytp-caption-window-rollup,
#player-captions-container,
.captions-text,
.ytp-cued-thumbnail-overlay,
.ytp-endscreen-content,
.ytp-ce-element,
.ytp-pause-overlay,
.ytp-pause-overlay-container,
.ytp-scroll-min,
.ytp-suggestions,
.ytp-endscreen-paginate,
.ytp-show-tiles,
.html5-endscreen,
.ytp-ce-covering-overlay,
.ytp-ce-element-show,
.ytp-cards-teaser,
.ytp-cards-button,
.ytp-chrome-top,
.ytp-chrome-top-buttons,
.ytp-share-button,
.ytp-copylink-button,
.ytp-watermark,
.ytp-youtube-button,
.ytp-contextmenu,
[class*="caption"],
[class*="subtitle"],
[class*="endscreen"],
[class*="pause-overlay"],
video::cue,
::cue {
  display: none !important;
  visibility: hidden !important;
  opacity: 0 !important;
  font-size: 0px !important;
  height: 0 !important;
  width: 0 !important;
  pointer-events: none !important;
}
</style>
</head>
<body>
<div id="player-wrapper">
  <div id="player"></div>
</div>
<div id="touch-shield"></div>
<script>
var player = null;
window.player = null;
var finished = false, index = __INITIAL__, ids = [__LIST__], guard;

function notifyToggleControls(){
  try {
    window.location.href = 'ychicatube://toggle-controls';
  } catch(e){}
}

var shield = document.getElementById('touch-shield');
if(shield){
  shield.addEventListener('click', function(e){
    e.preventDefault();
    e.stopPropagation();
    notifyToggleControls();
  });
}

window.togglePlayPause = function(){
  try {
    if(window.player && typeof window.player.getPlayerState === 'function'){
      var state = window.player.getPlayerState();
      if(state === YT.PlayerState.PLAYING){
        window.player.pauseVideo();
        return false;
      } else {
        window.player.playVideo();
        return true;
      }
    }
  } catch(e){}
  return true;
};

window.playVideo = function(){
  try { if(window.player && typeof window.player.playVideo === 'function') window.player.playVideo(); } catch(e){}
};

window.pauseVideo = function(){
  try { if(window.player && typeof window.player.pauseVideo === 'function') window.player.pauseVideo(); } catch(e){}
};

window.playVideoId = function(id){
  try {
    if(ids.indexOf(id) !== -1){
      index = ids.indexOf(id);
    } else {
      ids.push(id);
      index = ids.length - 1;
    }
    if(window.player && typeof window.player.loadVideoById === 'function'){
      window.player.loadVideoById(id);
      try { window.player.playVideo(); } catch(e){}
      nukeCaptions();
    }
  } catch(err){
    console.error('yChicaTube: error loading video ' + id, err);
  }
};

function nukeCaptions(){
  try{
    if(window.player){
      if(typeof window.player.unloadModule === 'function'){
        window.player.unloadModule('captions');
        window.player.unloadModule('cc');
      }
      if(typeof window.player.setOption === 'function'){
        window.player.setOption('captions','track',{});
        window.player.setOption('cc','track',{});
        window.player.setOption('captions','reload',false);
        window.player.setOption('cc','reload',false);
        window.player.setOption('captions','fontSize',-1);
      }
    }
  }catch(e){}
  try{
    var tracks=document.querySelectorAll('track');
    for(var i=0;i<tracks.length;i++){ tracks[i].mode='disabled'; }
  }catch(e){}
  try{
    var els=document.querySelectorAll('.caption-window, .ytp-caption-window-bottom, .ytp-caption-segment, [class*="caption"], [class*="subtitle"], [class*="endscreen"], [class*="pause-overlay"], #player-captions-container, .ytp-caption-window-rollup');
    for(var j=0;j<els.length;j++){
      els[j].style.setProperty('display','none','important');
      els[j].style.setProperty('visibility','hidden','important');
      els[j].style.setProperty('opacity','0','important');
      els[j].style.setProperty('pointer-events','none','important');
      els[j].style.setProperty('height','0','important');
    }
  }catch(e){}
}

function initPlayer(){
  if(window.player) return;
  console.info('yChicaTube: initializing player');
  var currentId = ids.length > 0 ? ids[index] : '';
  if(!currentId){
    console.error('yChicaTube: no valid video id');
    return;
  }
  try {
    window.player = new YT.Player('player',{
      videoId: currentId,
      width: '100%',
      height: '100%',
      host: 'https://www.youtube-nocookie.com',
      playerVars: {
        autoplay: 1,
        controls: 0,
        rel: 0,
        playsinline: 1,
        enablejsapi: 1,
        modestbranding: 1,
        disablekb: 1,
        iv_load_policy: 3,
        cc_load_policy: 0,
        cc_lang_pref: 'none',
        fs: 0,
        origin: 'https://www.youtube-nocookie.com',
        widget_referrer: 'https://www.youtube-nocookie.com'
      },
      events: {
        onReady: onReady,
        onStateChange: onState,
        onError: onError
      }
    });
    player = window.player;
  } catch(e){
    console.error('yChicaTube: error constructing YT.Player', e);
  }
}

window.onYouTubeIframeAPIReady = function(){
  console.info('yChicaTube: IFrame API ready callback');
  initPlayer();
};

if(window.YT && window.YT.Player){
  initPlayer();
}

function onReady(e){
  console.info('yChicaTube: player ready');
  nukeCaptions();
  try {
    e.target.playVideo();
  } catch(err){}
  setTimeout(function(){
    try {
      if(window.player && typeof window.player.getPlayerState === 'function'){
        if(window.player.getPlayerState() !== YT.PlayerState.PLAYING && window.player.getPlayerState() !== YT.PlayerState.BUFFERING){
          window.player.playVideo();
        }
      }
    } catch(e){}
  }, 400);
}

function onError(e){
  console.error('yChicaTube: YouTube player error '+e.data);
  setTimeout(next, 2000);
}

function onState(e){
  console.info('yChicaTube: player state '+e.data);
  nukeCaptions();
  if(finished) return;
  if(e.data===YT.PlayerState.ENDED){
    clearTimeout(guard);
    next();
  }
}

function next(){
  if(finished) return;
  index++;
  if(index<ids.length){
    if(window.player && typeof window.player.loadVideoById === 'function'){
      window.player.loadVideoById(ids[index]);
      try { window.player.playVideo(); } catch(e){}
    }
    nukeCaptions();
  }else{
    finished=true;
    clearTimeout(guard);
    document.body.innerHTML='<div style="height:100%;display:grid;place-items:center;color:white;font:600 22px system-ui;text-align:center;padding:30px">__STORIES_ENDED__</div>';
  }
}

function endSession(){
  finished=true;
  clearTimeout(guard);
  if(window.player && typeof window.player.stopVideo === 'function') window.player.stopVideo();
  document.body.innerHTML='<div style="height:100%;display:grid;place-items:center;color:white;font:600 22px system-ui;text-align:center;padding:30px">__NEXT_MEAL__</div>';
}

setInterval(nukeCaptions, 300);
</script>
<script src="https://www.youtube.com/iframe_api"></script>
</body>
</html>

""".Replace("__INITIAL__", initial.ToString())
   .Replace("__LIST__", list)
   .Replace("__STORIES_ENDED__", storiesEndedText)
   .Replace("__NEXT_MEAL__", nextMealText);
    }
}
