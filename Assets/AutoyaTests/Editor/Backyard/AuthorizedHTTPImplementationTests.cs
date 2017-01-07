using System;
using System.Collections.Generic;
using AutoyaFramework;
using Miyamasu;
using UnityEngine;



/**
	tests for Autoya Authorized HTTP.
	Autoya strongly handle these server-related errors which comes from game-server.
	
	these test codes are depends on online env + "https://httpbin.org".
*/
public class AuthorizedHTTPImplementationTests : MiyamasuTestRunner {
	[MSetup] public void Setup () {
		var authorized = false;
		Action onMainThread = () => {
			var dataPath = string.Empty;
			Debug.LogWarning("自動的に初期化されてるので、特定のクラスを渡してハンドラぶっ叩くとかをしたほうがいいかもしれない。渡した時点でいろんなハンドラがぶっ叩かれるほうが制御が楽というか。勝手に見つけてくるんでもいいんだけど。自分で初期化しなくなったことでいろいろある。というか[まとめて登録]みたいな感じか。");
			Autoya.TestEntryPoint(dataPath);
			
			Autoya.Auth_SetOnLoginSucceeded(
				() => {
					authorized = true;
				}
			);
			
			Autoya.Auth_SetOnAuthFailed(
				(conId, reason) => {
					return false;
				}
			);
		};
		RunOnMainThread(onMainThread);
		
		WaitUntil(
			() => {
				return authorized;
			}, 
			5, 
			"failed to auth."
		);
		Assert(Autoya.Auth_IsLoggedIn(), "not logged in.");
	}
	
	[MTeardown] public void Teardown () {
		RunOnMainThread(Autoya.Shutdown);
	}

	[MTest] public void AutoyaHTTPGet () {
		var result = string.Empty;
		var connectionId = Autoya.Http_Get(
			"https://httpbin.org/get", 
			(string conId, string resultData) => {
				result = "done!:" + resultData;
			},
			(string conId, int code, string reason) => {
				Assert(false, "failed. code:" + code + " reason:" + reason);
			}
		);

		WaitUntil(
			() => !string.IsNullOrEmpty(result), 
			5
		);
	}

	[MTest] public void AutoyaHTTPGetWithAdditionalHeader () {
		var result = string.Empty;
		var connectionId = Autoya.Http_Get(
			"https://httpbin.org/headers", 
			(string conId, string resultData) => {
				result = resultData;
			},
			(string conId, int code, string reason) => {
				Assert(false, "failed. code:" + code + " reason:" + reason);
			},
			new Dictionary<string, string>{
				{"Hello", "World"}
			}
		);

		WaitUntil(
			() => (result.Contains("Hello") && result.Contains("World")), 
			5
		);
	}

	[MTest] public void AutoyaHTTPGetFailWith404 () {
		var resultCode = 0;
		
		var connectionId = Autoya.Http_Get(
			"https://httpbin.org/status/404", 
			(string conId, string resultData) => {
				Assert(false, "unexpected succeeded. resultData:" + resultData);
			},
			(string conId, int code, string reason) => {
				resultCode = code;
			}
		);

		WaitUntil(
			() => (resultCode != 0), 
			5
		);
		
		// result should be have reason,
		Assert(resultCode == 404, "code unmatched. resultCode:" + resultCode);
	}

	[MTest] public void AutoyaHTTPGetFailWithUnauth () {
		var unauthReason = string.Empty;

		// set unauthorized method callback.
		Autoya.Auth_SetOnAuthFailed(
			(conId, reason) => {
				unauthReason = reason;
				
				// if want to start re-login, return true.
				return true;
			}
		);

		/*
			dummy server returns 401 forcely.
		*/
		var connectionId = Autoya.Http_Get(
			"https://httpbin.org/status/401", 
			(string conId, string resultData) => {
				Assert(false, "unexpected succeeded. resultData:" + resultData);
			},
			(string conId, int code, string reason) => {
				// do nothing.
			}
		);

		WaitUntil(
			() => !string.IsNullOrEmpty(unauthReason), 
			5
		);
		
		Assert(!string.IsNullOrEmpty(unauthReason), "code unmatched. unauthReason:" + unauthReason);
	}

	[MTest] public void AutoyaHTTPGetFailWithTimeout () {
		var failedCode = -1;
		var timeoutError = string.Empty;
		/*
			fake server should be response in 1msec. 
			server responses 1 sec later.
			it is impossible.
		*/
		var connectionId = Autoya.Http_Get(
			"https://httpbin.org/delay/1", 
			(string conId, string resultData) => {
				Assert(false, "got success result.");
			},
			(string conId, int code, string reason) => {
				failedCode = code;
				timeoutError = reason;
			},
			null,
			0.0001
		);

		WaitUntil(
			() => {
				return !string.IsNullOrEmpty(timeoutError);
			}, 
			3
		);

		Assert(failedCode == BackyardSettings.HTTP_TIMEOUT_CODE, "unmatch. failedCode:" + failedCode + " message:" + timeoutError);
	}

	[MTest] public void AutoyaHTTPPost () {
		var result = string.Empty;
		var connectionId = Autoya.Http_Post(
			"https://httpbin.org/post", 
			"data",
			(string conId, string resultData) => {
				result = "done!:" + resultData;
			},
			(string conId, int code, string reason) => {
				// do nothing.
			}
		);

		WaitUntil(
			() => !string.IsNullOrEmpty(result), 
			5
		);
	}

	/*
		target test site does not support show post request. hmmm,,,
	*/
	// [MTest] public void AutoyaHTTPPostWithAdditionalHeader () {
	// 	var result = string.Empty;
	// 	var connectionId = Autoya.Http_Post(
	// 		"https://httpbin.org/headers", 
	// 		"data",
	// 		(string conId, string resultData) => {
	// 			TestLogger.Log("resultData:" + resultData);
	// 			result = resultData;
	// 		},
	// 		(string conId, int code, string reason) => {
	// 			TestLogger.Log("fmmmm,,,,, AutoyaHTTPPostWithAdditionalHeader failed conId:" + conId + " reason:" + reason);
	// 			// do nothing.
	// 		},
	// 		new Dictionary<string, string>{
	// 			{"Hello", "World"}
	// 		}
	// 	);

	// 	var wait = WaitUntil(
	// 		() => (result.Contains("Hello") && result.Contains("World")), 
	// 		5
	// 	);
	// 	if (!wait) return false; 
		
	// 	return true;
	// }

	[MTest] public void AutoyaHTTPPostFailWith404 () {
		var resultCode = 0;
		
		var connectionId = Autoya.Http_Post(
			"https://httpbin.org/status/404",
			"data", 
			(string conId, string resultData) => {
				// do nothing.
			},
			(string conId, int code, string reason) => {
				resultCode = code;
			}
		);

		WaitUntil(
			() => (resultCode != 0), 
			5
		);
		
		// result should be have reason,
		Assert(resultCode == 404, "code unmatched. resultCode:" + resultCode);
	}

	[MTest] public void AutoyaHTTPPostFailWithUnauth () {
		var unauthReason = string.Empty;

		// set unauthorized method callback.
		Autoya.Auth_SetOnAuthFailed(
			(conId, reason) => {
				unauthReason = reason;
				
				// if want to start re-login, return true.
				return true;
			}
		);

		/*
			dummy server returns 401 definitely.
		*/
		var connectionId = Autoya.Http_Post(
			"https://httpbin.org/status/401",
			"data", 
			(string conId, string resultData) => {
				// do nothing.
			},
			(string conId, int code, string reason) => {
				// do nothing.
			}
		);

		WaitUntil(
			() => !string.IsNullOrEmpty(unauthReason), 
			3,
			"timeout."
		);
		
		Assert(!string.IsNullOrEmpty(unauthReason), "unauthReason is empty.");
	}

	[MTest] public void AutoyaHTTPPostFailWithTimeout () {
		var timeoutError = string.Empty;
		/*
			fake server should be response 1msec
		*/
		var connectionId = Autoya.Http_Post(
			"https://httpbin.org/delay/1",
			"data",
			(string conId, string resultData) => {
				Assert(false, "got success result.");
			},
			(string conId, int code, string reason) => {
				Assert(code == BackyardSettings.HTTP_TIMEOUT_CODE, "not match. code:" + code + " reason:" + reason);
				timeoutError = reason;
			},
			null,
			0.0001// 1ms
		);

		WaitUntil(
			() => {
				return !string.IsNullOrEmpty(timeoutError);
			}, 
			3
		);
	}
	
}
