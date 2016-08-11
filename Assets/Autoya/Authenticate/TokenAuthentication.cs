using UnityEngine;
using Connections.HTTP;
using System;
using System.Collections.Generic;
using UniRx;

namespace AutoyaFramework {
	public partial class Autoya {
		/*
			authentication implementation.
				this feature controls the flow of authentication.

			Authentination in Autoya is based on the scenerio: "stored token then use it for login".
				on Initial Running:
					client(vacant) -> server
						get first token then get identitiy of client frm server.
						then store id and token into client.

				on LogIn:
					client(id + token) -> server
						at login with id + token.

				on LoggedIn:
					client(id + token) -> server
						at various connection.

				on LogOut:
					client(id + token) becomes client(id)
						
				on Erase:("App Erase" or "App Data Erase")
					client(id + token) becomes client(vacant)


			There are 3 way flow to gettting login.
				1.get id and token
				2.login with token

				or

				1.get token
				2.login with token

				or 

				1.load token from app
				2.login with token
		*/
		private string _token;
		private void InitializeTokenAuth () {
			_loginState = LoginState.LOGGED_OUT;

			/*
				set default handlers.
			*/
			OnLoginSucceeded = token => {
				LoggedIn(token);
			};

			OnAuthFailed = (conId, reason) => {
				LogOut();
				return false;
			};

			LoadTokenThenLogin();
		}

		private int Progress () {
			return (int)_loginState;
		}

		private void LoggedIn (string token) {
			Debug.Assert(!(string.IsNullOrEmpty(token)));

			_token = token;
			_loginState = LoginState.LOGGED_IN;
		}

		private void LogOut () {
			_loginState = LoginState.LOGGED_OUT;
			RevokeToken();
		}

		private void LoadTokenThenLogin () {
			var identityOrEmpty = LoadIdentity();
			var tokenCandidate = LoadToken();

			/*
				if token is already stored and valid, goes to login.
				else, get token then login.
			*/
			var isValid = IsTokenValid(tokenCandidate);
			
			if (isValid) {
				Debug.LogWarning("(maybe) valid token found. start login with it.");
				AttemptLoginByTokenCandidate(tokenCandidate);
			} else {
				Debug.LogWarning("no token found. get token then login.");
				GetTokenThenLogin(identityOrEmpty);
			}
		}

		private bool SaveIdentity (string identity) {
			return _autoyaFilePersistence.Update(
				AutoyaConsts.AUTH_STORED_FRAMEWORK_DOMAIN, 
				AutoyaConsts.AUTH_STORED_IDENTITY_FILENAME,
				identity
			);
		}
		private string LoadIdentity () {
			return _autoyaFilePersistence.Load(
				AutoyaConsts.AUTH_STORED_FRAMEWORK_DOMAIN, 
				AutoyaConsts.AUTH_STORED_TOKEN_FILENAME
			); 
		}

		private bool SaveToken (string newTokenCandidate) {
			return _autoyaFilePersistence.Update(
				AutoyaConsts.AUTH_STORED_FRAMEWORK_DOMAIN, 
				AutoyaConsts.AUTH_STORED_TOKEN_FILENAME,
				newTokenCandidate
			);
		}

		private string LoadToken () {
			return _autoyaFilePersistence.Load(
				AutoyaConsts.AUTH_STORED_FRAMEWORK_DOMAIN, 
				AutoyaConsts.AUTH_STORED_TOKEN_FILENAME
			);
		}

		private void GetTokenThenLogin (string identity) {
			_loginState = LoginState.GETTING_TOKEN;

			var tokenUrl = AutoyaConsts.AUTH_URL_TOKEN;
			var tokenHttp = new HTTPConnection();
			var tokenConnectionId = AutoyaConsts.AUTH_CONNECTIONID_GETTOKEN_PREFIX + Guid.NewGuid().ToString();
			
			Debug.LogError("内部に保存していたidentityをアレしたものを使って、トークンを作り出す。");

			Observable.FromCoroutine(
				() => tokenHttp.Get(
					tokenConnectionId,
					null,
					tokenUrl,
					(conId, code, responseHeaders, data) => {
						EvaluateTokenResult(conId, responseHeaders, code, data);
					},
					(conId, code, failedReason, responseHeaders) => {
						EvaluateTokenResult(conId, responseHeaders, code, failedReason);
					}
				)
			).Timeout(
				TimeSpan.FromSeconds(AutoyaConsts.HTTP_TIMEOUT_SEC)
			).Subscribe(
				_ => {},
				ex => {
					var errorType = ex.GetType();

					switch (errorType.ToString()) {
						case AutoyaConsts.AUTH_HTTP_INTERNALERROR_TYPE_TIMEOUT: {
							EvaluateTokenResult(tokenConnectionId, new Dictionary<string, string>(), AutoyaConsts.AUTH_HTTP_INTERNALERROR_CODE_TIMEOUT, "timeout:" + ex.ToString());
							break;
						}
						default: {
							throw new Exception("failed to get token by undefined reason:" + ex.Message);
						}
					}
				}
			);
		}

		private void EvaluateTokenResult (string tokenConnectionId, Dictionary<string, string> responseHeaders, int responseCode, string resultDataOrFailedReason) {
			// 取得したtokenを検査する必要がある。ヘッダとかで検証とかいろいろ。 検証メソッドとか外に出せばいいことあるかな。
			Debug.LogWarning("EvaluateTokenResult!! " + " tokenConnectionId:" + tokenConnectionId + " responseCode:" + responseCode + " resultDataOrFailedReason:" + resultDataOrFailedReason);

			ErrorFlowHandling(
				tokenConnectionId,
				responseHeaders, 
				responseCode,  
				resultDataOrFailedReason, 
				(succeededConId, succeededData) => {
					var isValid = IsTokenValid(succeededData);
					
					if (isValid) {
						Debug.LogWarning("token取得に成功 succeededData:" + succeededData);
						var tokenCandidate = succeededData;
						UpdateTokenThenAttemptLogin(tokenCandidate);
					} else {
						Debug.LogError("未解決の、invalidなtokenだと見做せた場合の処理");
					}
				},
				(failedConId, failedCode, failedReason) => {
					_loginState = LoginState.LOGGED_OUT;
					
					if (IsInMaintenance(responseCode, responseHeaders)) {
						// in maintenance, do nothing here.
						return;
					}

					if (IsAuthFailed(responseCode, responseHeaders)) {
						// get token url should not return unauthorized response. do nothing here.
						return;
					}

					// other errors. 
					var shouldRetry = OnAuthFailed(tokenConnectionId, resultDataOrFailedReason);
					if (shouldRetry) {
						Debug.LogError("なんかtoken取得からリトライすべきなんだけどちょっとまってな1");
						// GetTokenThenLogin();
					} 
				}
			);
		}

		private bool IsTokenValid (string tokenCandidate) {
			if (string.IsNullOrEmpty(tokenCandidate)) return false; 
			return true;
		}


		/**
			login with token candidate.
		*/
		private void AttemptLoginByTokenCandidate (string tokenCandidate) {
			_loginState = LoginState.LOGGING_IN;

			// attempt login with token.
			var loginUrl = AutoyaConsts.AUTH_URL_LOGIN;
			var loginHeaders = new Dictionary<string, string>{
				{"token", tokenCandidate}
			};
			var loginHttp = new HTTPConnection();
			var loginConnectionId = AutoyaConsts.AUTH_CONNECTIONID_ATTEMPTLOGIN_PREFIX + Guid.NewGuid().ToString();
			
			Observable.FromCoroutine(
				_ => loginHttp.Get(
					loginConnectionId,
					loginHeaders,
					loginUrl,
					(conId, code, responseHeaders, data) => {
						EvaluateLoginResult(conId, responseHeaders, code, data);
					},
					(conId, code, failedReason, responseHeaders) => {
						EvaluateLoginResult(conId, responseHeaders, code, failedReason);
					}
				)
			).Timeout(
				TimeSpan.FromSeconds(AutoyaConsts.HTTP_TIMEOUT_SEC)
			).Subscribe(
				_ => {},
				ex => {
					var errorType = ex.GetType();

					switch (errorType.ToString()) {
						case AutoyaConsts.AUTH_HTTP_INTERNALERROR_TYPE_TIMEOUT: {
							EvaluateLoginResult(loginConnectionId, new Dictionary<string, string>(), AutoyaConsts.AUTH_HTTP_INTERNALERROR_CODE_TIMEOUT, "timeout:" + ex.ToString());
							break;
						}
						default: {
							throw new Exception("failed to get token by undefined reason:" + ex.Message);
						}
					}
				}
			);
		}

		private void EvaluateLoginResult (string loginConnectionId, Dictionary<string, string> responseHeaders, int responseCode, string resultDataOrFailedReason) {
			// ログイン時のresponseHeadersに入っている情報について、ここで判別前〜後に扱う必要がある。
			ErrorFlowHandling(
				loginConnectionId,
				responseHeaders, 
				responseCode,  
				resultDataOrFailedReason, 
				(succeededConId, succeededData) => {
					Debug.LogWarning("EvaluateLoginResult tokenを使ったログイン通信に成功。401チェックも突破。これでログイン動作が終わることになる。");
					
					// ここで使ってたcandidate = 保存されてるやつ を、_tokenにセットして良さげ。
					// なんかサーバからtokenのハッシュとか渡してきて、ここで照合すると良いかもっていう気が少しした。
					var savedToken = LoadToken();
					OnLoginSucceeded(savedToken);
				},
				(failedConId, failedCode, failedReason) => {
					// if Unauthorized, OnAuthFailed is already called.
					if (IsAuthFailed(responseCode, responseHeaders)) return;
			
					_loginState = LoginState.LOGGED_OUT;

					/*
						we should handling NOT 401(Unauthorized) result.
					*/

					// tokenはあったんだけど通信失敗とかで予定が狂ったケースか。
					// tokenはあるんで、エラーわけを細かくやって、なんともできなかったら再チャレンジっていうコースな気がする。
					
					var shouldRetry = OnAuthFailed(loginConnectionId, resultDataOrFailedReason);
					if (shouldRetry) {
						Debug.LogError("ログイン失敗、リトライすべきなんだけどちょっとまってな2");
						// LoadTokenThenLogin();
					}
				}
			);
		}
		
		private void UpdateTokenThenAttemptLogin (string gotNewToken) {
			var isSaved = SaveToken(gotNewToken);
			if (isSaved) {
				LoadTokenThenLogin();
			} else {
				Debug.LogError("tokenのSaveに失敗、この辺、保存周りのイベントと連携させないとな〜〜〜");
			}
		}
		private void RevokeToken () {
			_token = string.Empty;
			var isRevoked = SaveToken(string.Empty);
			if (!isRevoked) Debug.LogError("revokeに失敗、この辺、保存周りのイベントと連携させないとな〜〜〜");
		}


		/*
			public auth APIs
		*/

		public static int Auth_Progress () {
			return autoya.Progress();
		}

		public static void Auth_AttemptLogIn () {
			autoya.LoadTokenThenLogin();
		}

		public static void Auth_SetOnLoginSucceeded (Action onAuthSucceeded) {
			autoya.OnLoginSucceeded = token => {
				autoya.LoggedIn(token);
				onAuthSucceeded();
			};
			
			// if already logged in, fire immediately.
			if (Auth_IsLoggedIn()) onAuthSucceeded();
        }

		public static void Auth_SetOnAuthFailed (Func<string, string, bool> onAuthFailed) {
            autoya.OnAuthFailed = (conId, reason) => {
				autoya.LogOut();
				return onAuthFailed(conId, reason);
			};
        }

		public static void Auth_Logout () {
			autoya.LogOut();
		}
		
		private Action<string> OnLoginSucceeded;

		/**
			this method will be called when Autoya encountered "auth failed".
			caused by: received 401 response by some reason will raise this method.

			1.server returns 401 as the result of login request.
			2.server returns 401 as the result of usual http connection.

			and this method DOES NOT FIRE when logout intentionally.
		*/
		private Func<string, string, bool> OnAuthFailed;


		
		public enum LoginState : int {
			LOGGED_OUT,
			GETTING_TOKEN,
			LOGGING_IN,
			LOGGED_IN,
		}

		private LoginState _loginState;

		public static bool Auth_IsLoggedIn () {
			if (string.IsNullOrEmpty(autoya._token)) return false;
			if (autoya._loginState != LoginState.LOGGED_IN) return false; 
			return true;
		}
		
		/*
			test methods.
		*/
		public static void Auth_Test_CreateAuthError () {
			/*
				generate fake response for generate fake accidential logout error.
			*/
			autoya.ErrorFlowHandling(
				"Auth_Test_AccidentialLogout_ConnectionId", 
				new Dictionary<string, string>(),
				401, 
				"Auth_Test_AccidentialLogout test error", 
				(conId, data) => {}, 
				(conId, code, reason) => {}
			);
		}
		
	}
}