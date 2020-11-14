namespace EasterFolly
{
	public interface IJsonAssetsApi
	{
		void LoadAssets(string path);

		int GetObjectId(string name);
		int GetCropId(string name);
	}
}
