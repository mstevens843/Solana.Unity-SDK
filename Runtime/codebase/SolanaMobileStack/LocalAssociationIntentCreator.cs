using UnityEngine;

// ReSharper disable once CheckNamespace

public static class LocalAssociationIntentCreator
{
    
    private const string TAG = "[IntentCreator]";

    public static AndroidJavaObject CreateAssociationIntent(string associationToken, int port)
    {
        var intent = new AndroidJavaObject("android.content.Intent");
        intent.Call<AndroidJavaObject>("setAction", "android.intent.action.VIEW");
        intent.Call<AndroidJavaObject>("addCategory", "android.intent.category.BROWSABLE");
        var url = $"{AssociationContract.SchemeMobileWalletAdapter}:/" +
                  $"{AssociationContract.LocalPathSuffix}?association={associationToken}&port={port}&v=1";
        Debug.Log($"{TAG} CreateAssociationIntent | url={url} port={port} token_len={associationToken.Length}");
        var uriClass = new AndroidJavaClass("android.net.Uri");
        var uriData = uriClass.CallStatic<AndroidJavaObject>("parse", url);
        intent.Call<AndroidJavaObject>("setData", uriData);
        //intent.Call<AndroidJavaObject>("addFlags", 0x14000000);
        return intent;
    }
}
