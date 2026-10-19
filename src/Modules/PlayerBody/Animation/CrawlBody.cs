using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace WildWasteland.Modules.PlayerBody
{
    internal sealed partial class Body
    {
        AnimationMixerPlayable _crawlMix;
        AnimationClipPlayable _crawlClip;
        AnimationPlayableOutput _crawlOutput;
        float _crawlTime, _crawlBlend;
        Vector3 _crawlHips;
        internal bool CrawlMotion { get { return _crawlMix.IsValid(); } }
        partial void CaptureCrawlMotion();

        bool LateCrawl(View view, float dt)
        {
            if (!Crawling.Active || Crawling.Clip == null) { EndCrawl(); return false; }
            if (_animator == null || !_animator.isHuman || !_graph.IsValid()) return false;
            Transform hips;
            if (!Bones.TryGetValue("mixamorig:Hips", out hips)) return false;
            if (!_crawlMix.IsValid())
            {
                _crawlOutput = (AnimationPlayableOutput)_graph.GetOutput(0);
                _crawlMix = AnimationMixerPlayable.Create(_graph, 2);
                _crawlClip = AnimationClipPlayable.Create(_graph, Crawling.Clip);
                _crawlClip.SetApplyFootIK(false); _crawlClip.SetApplyPlayableIK(false); _crawlClip.SetSpeed(0);
                _graph.Connect(_layers, 0, _crawlMix, 0); _graph.Connect(_crawlClip, 0, _crawlMix, 1);
                _crawlMix.SetInputWeight(0, 0); _crawlMix.SetInputWeight(1, 1); _crawlOutput.SetSourcePlayable(_crawlMix);
                _crawlTime = _crawlBlend = 0; _crawlHips = hips.localPosition;
                _reloading = false; _throwUntil = _pumpUntil = _reloadUntil = 0;
                _jumpPhase = ""; _prone = 1; _crouch = 0; _inCar = false; _sway = Vector3.zero;
                UpdateProp(""); StartPoseFade(.18f, false);
            }
            Vector3 forward = Game.PlayerCamera.forward; forward.y = 0;
            if (forward.sqrMagnitude < .001f) forward = Game.Player.transform.forward;
            var yaw = Quaternion.LookRotation(forward.normalized, Vector3.up);
            float speed = Vector3.Dot(Game.Velocity, yaw * Vector3.forward);
            if (!Game.Paused && Mathf.Abs(speed) > .05f) _crawlTime = Mathf.Repeat(_crawlTime + dt * Mathf.Clamp(speed / .8f, -1.5f, 1.5f), Crawling.Clip.length);
            _crawlClip.SetTime(_crawlTime); _animator.enabled = true;
            Root.transform.SetPositionAndRotation(Game.Player.transform.position - Vector3.up * (Game.StandingHeight * .5f), yaw);
            Root.transform.localScale = Vector3.one; _anim.localPosition = Vector3.zero; _anim.localRotation = Quaternion.identity;
            _graph.Evaluate(0); if (_rootMotion != null) _rootMotion.Take();
            _crawlBlend = Mathf.Min(1, _crawlBlend + dt / .18f);
            hips.localPosition = Vector3.Lerp(_crawlHips, hips.localPosition, _crawlBlend);
            ApplyPoseFade(dt, null); CapturePose(null); ShowProp(false); _lastView = view;
            CaptureCrawlMotion();
            Transform head;
            if (view == View.FirstPerson && Bones.TryGetValue("mixamorig:Head", out head))
            {
                Vector3 offset = Game.PlayerCamera.position - yaw * Vector3.forward * PlayerBodyModule.BodyBack.Value - head.position;
                offset.y = 0; Root.transform.position += offset;
            }
            return true;
        }
        internal void EndCrawl()
        {
            if (!_graph.IsValid() || !_crawlMix.IsValid()) return;
            _crawlOutput.SetSourcePlayable(_layers); _graph.Disconnect(_crawlMix, 0); _graph.Disconnect(_crawlMix, 1);
            _crawlMix.Destroy(); if (_crawlClip.IsValid()) _crawlClip.Destroy();
            _prone = 0; _snap = true; StartPoseFade(.18f, false);
        }
    }
}
