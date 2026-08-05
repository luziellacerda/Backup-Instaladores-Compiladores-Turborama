#pragma once

#include <map>
#include <string>

struct PixOwnerSettings
{
	int schemaVersion = 1;
	bool enabled = false;
	std::string provider = "mercadopago";
	std::string accountId;
	std::string storeExternalId = "TURBORAMALOJA01";
	std::string storeName = "TurboRama";
	std::string posExternalId = "TURBORAMAKIOSK01";
	std::string posName = "TurboRama Kiosk";
	std::string postalCode;
	std::string streetNumber;
	std::string reference = "TurboRama";
	std::string adapterBaseUrl = "http://127.0.0.1:8765/";
	std::string adapterProviderId = "meu-banco";
	std::map<int, long long> pricesCents = {
		{ 15, 750 }, { 30, 1500 }, { 45, 2250 }, { 60, 3000 }, { 120, 6000 }
	};
};

class PixAgentManager
{
public:
	static PixOwnerSettings loadOwnerSettings();
	static bool saveOwnerSettings(const PixOwnerSettings& settings, const std::string& newAccessToken, std::string& error);
	static bool validateOwnerSettings(const PixOwnerSettings& settings, std::string& error);
	static bool runSelfTest(std::string& error);
	static bool hasProtectedToken();
	static bool startIfConfigured(std::string* error = nullptr);
	static bool superviseIfConfigured(std::string* error = nullptr);
	static bool restartIfConfigured(std::string& error);
	static std::string statusText();
	static std::string bridgeDirectory();
	static std::string agentExecutable();

private:
	static bool protectAndSaveToken(const std::string& token, std::string& error);
	static bool stopExpectedAgent();
};
