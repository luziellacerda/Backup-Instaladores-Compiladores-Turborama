#pragma once

#include <string>

// Uma unica camada nativa, sem foco e sempre no topo. Ela e compartilhada
// pelas telas do EmulationStation e pela supervisao dos jogos externos.
namespace CreditWarningOverlay
{
	void show(const std::string& message);
	void update();
	bool isVisible();
}
