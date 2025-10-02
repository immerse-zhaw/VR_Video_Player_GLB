using UnityEngine;
using UnityEngine.Video;

public class VideoManager : MonoBehaviour
{
	public VideoPlayer videoPlayer2D;
	public VideoPlayer videoPlayer3D;
	public Material skyboxMaterial;
	public Material defaultSkyboxMaterial;
	private bool is3DMode = false;

	void Awake()
	{
		if (videoPlayer2D == null)
			videoPlayer2D = GetComponent<VideoPlayer>();

		if (videoPlayer3D == null)
		{
			GameObject moviePlayer3D = GameObject.Find("3DMoviePlayer");
			if (moviePlayer3D != null)
				videoPlayer3D = moviePlayer3D.GetComponent<VideoPlayer>();
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
		if (is3DMode)
		{
			videoPlayer3D.source = VideoSource.Url;
			videoPlayer3D.url = path;
			videoPlayer3D.Prepare();
			videoPlayer3D.prepareCompleted += OnVideoPrepared3D;
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

	private void OnVideoPrepared3D(VideoPlayer vp)
	{
		vp.prepareCompleted -= OnVideoPrepared3D;
		vp.Play();

		RenderSettings.skybox = skyboxMaterial;
		skyboxMaterial.mainTexture = vp.targetTexture;
	}

	public void PauseVideo()
	{
		if (is3DMode && videoPlayer3D.isPlaying)
			videoPlayer3D.Pause();
		else if (!is3DMode && videoPlayer2D.isPlaying)
			videoPlayer2D.Pause();
	}

	public void ResumeVideo()
	{
		if (is3DMode && !videoPlayer3D.isPlaying)
			videoPlayer3D.Play();
		else if (!is3DMode && !videoPlayer2D.isPlaying)
			videoPlayer2D.Play();
	}

	public void Toggle2D(bool enabled)
	{
		videoPlayer2D.gameObject.SetActive(enabled);
	}

	public void Toggle3D(bool enabled)
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

	public void ToggleVideoMode(bool enable3D)
	{
		// Store current playback state
		bool wasPlaying = false;
		double currentTime = 0;
		string currentUrl = "";

		if (is3DMode && videoPlayer3D != null)
		{
			wasPlaying = videoPlayer3D.isPlaying;
			currentTime = videoPlayer3D.time;
			currentUrl = videoPlayer3D.url;
			videoPlayer3D.Stop();
		}
		else if (!is3DMode && videoPlayer2D != null)
		{
			wasPlaying = videoPlayer2D.isPlaying;
			currentTime = videoPlayer2D.time;
			currentUrl = videoPlayer2D.url;
			videoPlayer2D.Stop();
		}

		is3DMode = enable3D;

		if (is3DMode)
		{
			// Switch to 3D mode
			videoPlayer2D.gameObject.SetActive(false);
			videoPlayer3D.gameObject.SetActive(true);

			RenderSettings.skybox = skyboxMaterial;
			if (!string.IsNullOrEmpty(currentUrl))
			{
				videoPlayer3D.source = VideoSource.Url;
				videoPlayer3D.url = currentUrl;
				videoPlayer3D.Prepare();
				videoPlayer3D.prepareCompleted += (vp) => {
					vp.prepareCompleted -= OnVideoPrepared3D;
					vp.time = currentTime;
					if (wasPlaying) vp.Play();
					OnVideoPrepared3D(vp);
				};
			}
		}
		else
		{
			// Switch to 2D mode
			videoPlayer3D.gameObject.SetActive(false);
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
