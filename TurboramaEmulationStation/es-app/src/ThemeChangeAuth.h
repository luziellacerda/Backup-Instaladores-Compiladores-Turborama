#pragma once
#ifndef ES_APP_THEME_CHANGE_AUTH_H
#define ES_APP_THEME_CHANGE_AUTH_H

#include <string>

// Credencial exclusiva para liberar a troca do conjunto de tema.
// Quando ainda nao existe hash salvo, a senha inicial e "admin".
class ThemeChangeAuth
{
public:
	static bool verify(const std::string& password);
	static bool setPassword(const std::string& password);
	static bool hasCustomPassword();
};

#endif // ES_APP_THEME_CHANGE_AUTH_H
