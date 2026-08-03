#pragma once

#include <string>
#include <vector>

struct PixPackage
{
	int minutes = 0;
	long long amountCents = 0;
};

struct PixPublicOptions
{
	std::string provider;
	bool ready = false;
	bool productionEnabled = false;
	int paymentExpirationMinutes = 15;
	long long generatedAtUnixSeconds = 0;
	std::vector<PixPackage> packages;
};

enum class PixPurchaseState
{
	Generating,
	Pending,
	Approved,
	Completed,
	Cancelled,
	SecurityError,
	Rejected,
	Unknown
};

struct PixPurchaseInfo
{
	PixPurchaseState state = PixPurchaseState::Unknown;
	std::string qrImagePath;
	// Copia validada do PNG. A tela usa memoria para nao depender do cache de
	// caminhos do ImageComponent quando o arquivo acabou de ser criado.
	std::vector<unsigned char> qrImageData;
	// Matriz autenticada desenhada diretamente pelo Renderer. Este caminho nao
	// depende de PNG, FreeImage, cache de textura ou driver OpenGL.
	std::string qrMatrixPath;
	std::vector<unsigned char> qrModules;
	int qrModuleCount = 0;
	std::string error;
};

// Ponte local entre o agente PIX e o CreditManager. Nao conversa com banco nem guarda chaves.
class PixBridge
{
public:
	static std::vector<std::string> processApprovedCredits();
	static bool loadPublicOptions(PixPublicOptions& options, std::string& error);
	static bool createPurchaseRequest(const PixPackage& package, std::string& requestId, std::string& error);
	static PixPurchaseInfo getPurchaseInfo(const std::string& requestId);
	// Diagnostico somente leitura usado pelos testes de instalacao.
	static bool verifyApprovedEventFileForTest(const std::string& file, const std::string& root);
	// Reproduz a publicacao tardia do QR na mesma instancia do frontend.
	static bool runQrCacheRegressionTest();
};
