using UnityEngine;
using UnityEngine.Video;

public class VideoManager : MonoBehaviour
{
	public VideoPlayer videoPlayer2D;
	public VideoPlayer videoPlayer360;
	public Material skyboxMaterial;
	public Material defaultSkyboxMaterial;
	private bool is360Mode = false;

	void Awake()
	{
		if (videoPlayer2D == null)
			videoPlayer2D = GetComponent<VideoPlayer>();

		if (videoPlayer360 == null)
		{
			GameObject moviePlayer360 = GameObject.Find("360MoviePlayer");
			if (moviePlayer360 != null)
				videoPlayer360 = moviePlayer360.GetComponent<VideoPlayer>();
		}

		ApplyDefaultSkybox();
	}

	private void ApplyDefaultSkybox()
	{
		if (defaultSkyboxMaterial != null)
		{
			RenderSettings.skybox = defaultSkyboxMaterial;
		}
		else
		{
			Debug.LogError("Default skybox material is not assigned.");
		}
	}

	// Play a video from a file path (e.g., file:///C:/path/to/video.mp4)
	public void PlayVideo(string path)
	{
		if (is360Mode)
		{
			videoPlayer360.source = VideoSource.Url;
			videoPlayer360.url = path;
			videoPlayer360.Prepare();
			videoPlayer360.prepareCompleted += OnVideoPrepared360;
		}
		else
		{
			videoPlayer2D.source = VideoSource.Url;
			videoPlayer2D.url = path;
			videoPlayer2D.Prepare();
			videoPlayer2D.prepareCompleted += OnVideoPrepared2D;
		}
	}

	private void OnVideoPrepared2D(VideoPlayer vp)
	{
		vp.prepareCompleted -= OnVideoPrepared2D;
		vp.Play();
	}

	private void OnVideoPrepared360(VideoPlayer vp)
	{
		vp.prepareCompleted -= OnVideoPrepared360;
		vp.Play();

		RenderSettings.skybox = skyboxMaterial;
		skyboxMaterial.mainTexture = vp.targetTexture;
	}

	public void PauseVideo()
	{
		if (is360Mode && videoPlayer360.isPlaying)
			videoPlayer360.Pause();
		else if (!is360Mode && videoPlayer2D.isPlaying)
			videoPlayer2D.Pause();
	}

	public void ResumeVideo()
	{
		if (is360Mode && !videoPlayer360.isPlaying)
			videoPlayer360.Play();
		else if (!is360Mode && !videoPlayer2D.isPlaying)
			videoPlayer2D.Play();
	}

	public void Toggle2D(bool enabled)
	{
		videoPlayer2D.gameObject.SetActive(enabled);
	}

	public void Toggle360(bool enabled)
	{
		if (enabled)
		{
			RenderSettings.skybox = skyboxMaterial;
		}
		else
		{
			RenderSettings.skybox = defaultSkyboxMaterial;
		}
	}

	public void ToggleVideoMode(bool enable360)
	{
		// Store current playback state
		bool wasPlaying = false;
		double currentTime = 0;
		string currentUrl = "";

		if (is360Mode && videoPlayer360 != null)
		{
			wasPlaying = videoPlayer360.isPlaying;
			currentTime = videoPlayer360.time;
			currentUrl = videoPlayer360.url;
			videoPlayer360.Stop();
		}
		else if (!is360Mode && videoPlayer2D != null)
		{
			wasPlaying = videoPlayer2D.isPlaying;
			currentTime = videoPlayer2D.time;
			currentUrl = videoPlayer2D.url;
			videoPlayer2D.Stop();
		}

		is360Mode = enable360;

		if (is360Mode)
		{
			// Switch to 360 mode
			videoPlayer2D.gameObject.SetActive(false);
			videoPlayer360.gameObject.SetActive(true);

			RenderSettings.skybox = skyboxMaterial;
			if (!string.IsNullOrEmpty(currentUrl))
			{
				videoPlayer360.source = VideoSource.Url;
				videoPlayer360.url = currentUrl;
				videoPlayer360.Prepare();
				videoPlayer360.prepareCompleted += (vp) => {
					vp.prepareCompleted -= OnVideoPrepared360;
					vp.time = currentTime;
					if (wasPlaying) vp.Play();
					OnVideoPrepared360(vp);
				};
			}
		}
		else
		{
			// Switch to 2D mode
			videoPlayer360.gameObject.SetActive(false);
			videoPlayer2D.gameObject.SetActive(true);

			RenderSettings.skybox = defaultSkyboxMaterial;
			if (!string.IsNullOrEmpty(currentUrl))
			{
				videoPlayer2D.source = VideoSource.Url;
				videoPlayer2D.url = currentUrl;
				videoPlayer2D.Prepare();
				videoPlayer2D.prepareCompleted += (vp) => {
					vp.prepareCompleted -= OnVideoPrepared2D;
					vp.time = currentTime;
					if (wasPlaying) vp.Play();
					OnVideoPrepared2D(vp);
				};
			}
		}
	}
}
