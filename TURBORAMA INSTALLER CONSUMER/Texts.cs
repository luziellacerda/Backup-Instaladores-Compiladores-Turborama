using System;
using System.Collections.Generic;
using System.Globalization;

namespace InstallerHost
{
	// Token: 0x0200000D RID: 13
	public static class Texts
	{
		// Token: 0x06000056 RID: 86 RVA: 0x00006BE8 File Offset: 0x00004DE8
		public static string GetString(string key, params object[] args)
		{
			CultureInfo currentUICulture = CultureInfo.CurrentUICulture;
			string text;
			if (currentUICulture.Name.ToLowerInvariant().StartsWith("fr") && Texts.frenchStrings.ContainsKey(key))
			{
				text = Texts.frenchStrings[key];
			}
			else if (currentUICulture.Name.ToLowerInvariant().StartsWith("de") && Texts.germanStrings.ContainsKey(key))
			{
				text = Texts.germanStrings[key];
			}
			else if (currentUICulture.Name.ToLowerInvariant().StartsWith("es") && Texts.spanishStrings.ContainsKey(key))
			{
				text = Texts.spanishStrings[key];
			}
			else if (currentUICulture.Name.ToLowerInvariant().StartsWith("it") && Texts.italianStrings.ContainsKey(key))
			{
				text = Texts.italianStrings[key];
			}
			else if (currentUICulture.Name.ToLowerInvariant().StartsWith("bg") && Texts.bulgarianStrings.ContainsKey(key))
			{
				text = Texts.bulgarianStrings[key];
			}
			else if (currentUICulture.Name.ToLowerInvariant().StartsWith("pl") && Texts.polishStrings.ContainsKey(key))
			{
				text = Texts.polishStrings[key];
			}
			else
			{
				if (key != null)
				{
					switch (key.Length)
					{
					case 6:
						if (key == "vcText")
						{
							text = "Microsoft Visual C++ Runtimes Complete (2005-2022, x86 + x64) - selected";
							goto IL_06D2;
						}
						break;
					case 7:
					{
						char c = key[0];
						if (c != 'W')
						{
							if (c == 'd')
							{
								if (key == "dx9text")
								{
									text = "DirectX Complete: Legacy June 2010 + DirectX 11/12 via Windows Update - selected";
									goto IL_06D2;
								}
							}
						}
						else if (key == "Welcome")
						{
							text = "Welcome to the Turborama installation program";
							goto IL_06D2;
						}
						break;
					}
					case 9:
					{
						char c = key[0];
						if (c != 'A')
						{
							if (c == 'I')
							{
								if (key == "InstallDX")
								{
									text = "Installing DirectX Legacy and checking DirectX 11/12...";
									goto IL_06D2;
								}
							}
						}
						else if (key == "AgreeText")
						{
							text = "I accept the terms of the license agreement";
							goto IL_06D2;
						}
						break;
					}
					case 10:
					{
						char c = key[0];
						if (c <= 'L')
						{
							if (c != 'C')
							{
								if (c == 'L')
								{
									if (key == "LaunchFail")
									{
										text = "Failed to launch application: ";
										goto IL_06D2;
									}
								}
							}
							else if (key == "CancelSure")
							{
								text = "Are you sure that you want to cancel the installation ?";
								goto IL_06D2;
							}
						}
						else if (c == 'd' && key == "dokanyText")
						{
							text = "Dokan (used to mount XBOX images with CXBX)";
							goto IL_06D2;
						}
						break;
					}
					case 11:
					{
						char c = key[3];
						if (c <= 'c')
						{
							if (c != 'N')
							{
								if (c != 'R')
								{
									if (c == 'c')
									{
										if (key == "WelcomeText")
										{
											text = "This wizard will guide you through the installation of Turborama {0} {1} on your computer.\n\nIt is recommended to close all active applications before the next step.\n\nClick Next to continue or Cancel to exit the installer.";
											goto IL_06D2;
										}
									}
								}
								else if (key == "RunRetroBat")
								{
									text = "Run TurboRama.exe";
									goto IL_06D2;
								}
							}
							else if (key == "ExeNotFound")
							{
								text = "Executable not found: ";
								goto IL_06D2;
							}
						}
						else if (c <= 'i')
						{
							if (c != 'e')
							{
								if (c == 'i')
								{
									if (key == "ValidFolder")
									{
										text = "Please select a valid installation folder.";
										goto IL_06D2;
									}
								}
							}
							else if (key == "LicenseText")
							{
								text = Texts.licence;
								goto IL_06D2;
							}
						}
						else if (c != 'r')
						{
							if (c == 't')
							{
								if (key == "InstallFail")
								{
									text = "Installation failed: ";
									goto IL_06D2;
								}
								if (key == "InstallInfo")
								{
									text = "The installer program will install Turborama in the folder below.\n\nTo continue, click Next. If you want to specify another folder, Click Browse.";
									goto IL_06D2;
								}
							}
						}
						else if (key == "ExtractFail")
						{
							text = "Failed to extract installer data: ";
							goto IL_06D2;
						}
						break;
					}
					case 12:
					{
						char c = key[2];
						if (c <= 'i')
						{
							if (c != 'a')
							{
								if (c != 'c')
								{
									if (c == 'i')
									{
										if (key == "FailedFolder")
										{
											text = "Failed to create installation folder: ";
											goto IL_06D2;
										}
									}
								}
								else if (key == "LicenseIntro")
								{
									text = "Licence Agreement";
									goto IL_06D2;
								}
							}
							else if (key == "StartupError")
							{
								text = "Startup error occurred. See log file for details.";
								goto IL_06D2;
							}
						}
						else if (c != 'l')
						{
							if (c != 'n')
							{
								if (c == 's')
								{
									if (key == "InstallTitle")
									{
										text = "Destination folder";
										goto IL_06D2;
									}
								}
							}
							else if (key == "WindowsTitle")
							{
								text = "Turborama Installer ";
								goto IL_06D2;
							}
						}
						else if (key == "SelectFolder")
						{
							text = "Select the installation folder:";
							goto IL_06D2;
						}
						break;
					}
					case 13:
						if (key == "WaitingSelect")
						{
							text = "Waiting for selection...";
							goto IL_06D2;
						}
						break;
					case 14:
						if (key == "FolderNotEmpty")
						{
							text = "The selected folder is not empty. Please choose an empty folder or a new folder.";
							goto IL_06D2;
						}
						break;
					case 15:
						if (key == "InstallComplete")
						{
							text = "Installation completed successfully!";
							goto IL_06D2;
						}
						break;
					case 17:
					{
						char c = key[0];
						if (c != 'C')
						{
							if (c != 'I')
							{
								if (c == 'P')
								{
									if (key == "PrerequisiteIntro")
									{
										text = "All required runtimes are selected by default.";
										goto IL_06D2;
									}
								}
							}
							else if (key == "InstallFolderHint")
							{
								text = "The program requires at least 3.38 GB of free disk space.\n\nDo not use folders with spaces or special characters.";
								goto IL_06D2;
							}
						}
						else if (key == "CancelButtonTitle")
						{
							text = "Cancel";
							goto IL_06D2;
						}
						break;
					}
					case 18:
					{
						char c = key[0];
						if (c != 'D')
						{
							if (c == 'I')
							{
								if (key == "InstallComplete...")
								{
									text = "Installation complete...";
									goto IL_06D2;
								}
							}
						}
						else if (key == "DownloadAndInstall")
						{
							text = "Downloading and installing prerequisites...\r\nPlease wait...";
							goto IL_06D2;
						}
						break;
					}
					case 26:
						if (key == "InstallCompleteDescription")
						{
							text = "Turborama has been installed to your computer.\n\nPress finish to close this wizard";
							goto IL_06D2;
						}
						break;
					}
				}
				text = key;
			}
			IL_06D2:
			return string.Format(text, args);
		}

		// Token: 0x04000049 RID: 73
		private static string licence = "-- TURBORAMA LICENSE --\r\n\r\nTurborama is a Windows software distribution dedicated to retrogaming and emulation.\r\n\r\nCopyright (c) 2017-2019 Adrien Chalard \"Kayl\"\r\nCopyright (c) 2020-2026 Turborama Team\r\n\r\nTurborama is free and open source project. It should not be used for commercial purposes. \r\nIt is done by a team of enthusiasts in their free time mainly for fun.\r\nAll the code written by Turborama Team, unless covered by a licence from an upstream project, is given under the LGPL v3 licence.\r\nSee https://www.gnu.org/licenses.\r\n\r\nIt is not allowed to sell Turborama on a pre-installed machine or on any storage devices. \r\nTurborama includes softwares which cannot be associated with any commercial activities.\r\nShipping Turborama with additional proprietary and copyrighted content is illegal, strictly forbidden and strongly discouraged by the Turborama Team.\r\nOtherwise, you can start a new project off Turborama sources if you follow the same conditions.\r\n\r\nFinally, the license which concerns the entire Turborama Project as a work, in particular the written or graphic content broadcast on its various media, is conditioned by the terms of the CC BY-NC-SA 4.0 license.\r\nSee https://creativecommons.org/licenses/by-nc-sa/4.0.";

		// Token: 0x0400004A RID: 74
		private static Dictionary<string, string> bulgarianStrings = new Dictionary<string, string>
		{
			{ "Cancel", "Отказ" },
			{ "WindowsTitle", "Инсталатор Turborama " },
			{ "Next >", "Напред >" },
			{ "< Back", "< Назад" },
			{ "Browse...", "Преглед" },
			{ "Install", "Инсталирай" },
			{ "Welcome", "Добре дошли в програмата за инсталиране на Turborama" },
			{ "WelcomeText", "Този помощник ще ви преведе през инсталацията на Turborama {0} {1} на вашия компютър.\n\nПрепоръчва се да затворите всички активни приложения преди следващата стъпка.\n\nЩракнете върху „Напред“, за да продължите, или върху „Отказ“, за да излезете от инсталатора." },
			{ "CancelSure", "Сигурни ли сте, че искате да отмените инсталацията?" },
			{ "CancelButtonTitle", "Отказ" },
			{ "AgreeText", "Приемам условията на лицензионното споразумение" },
			{
				"LicenseText",
				Texts.licence
			},
			{ "LicenseIntro", "Лицензионно споразумение" },
			{ "SelectFolder", "Изберете папката за инсталиране:" },
			{ "InstallTitle", "Целева папка" },
			{ "InstallInfo", "Програмата за инсталиране ще инсталира Turborama в папката по-долу.\n\nЗа да продължите, щракнете върху „Напред“. Ако искате да изберете друга папка, щракнете върху „Преглед“." },
			{ "InstallFolderHint", "Програмата изисква поне 3.38 GB свободно дисково пространство.\n\nНе използвай папки с специални символи." },
			{ "FolderNotEmpty", "Избраната папка не е празна. Моля, изберете празна папка или създайте нова." },
			{ "ValidFolder", "Моля, изберете валидна папка за инсталиране." },
			{ "FailedFolder", "Неуспешно създаване на инсталационна папка: " },
			{ "ExtractFail", "Неуспешно извличане на данните на инсталатора: " },
			{ "InstallFail", "Инсталацията е неуспешна: " },
			{ "LaunchFail", "Неуспешно стартиране на приложението: " },
			{ "ExeNotFound", "Изпълнимият файл не е намерен: " },
			{ "InstallComplete", "Инсталацията е завършена..." },
			{ "RunRetroBat", "Стартирай TurboRama.exe" },
			{ "Finish", "Готово" },
			{ "Error", "Грешка" },
			{ "StartupError", "Възникна грешка при стартиране. Вижте файла с логове за повече информация." },
			{ "PrerequisiteIntro", "Изберете компонентите за инсталиране, преди да продължите." },
			{ "vcText", "Microsoft Visual C++ Runtimes Complete (2005-2022, x86 + x64) - selected" },
			{ "dx9text", "DirectX 9 (Наследена версия)" },
			{ "dokanyText", "Dokan (използва се за монтиране на XBOX образи с CXBX)" },
			{ "DownloadAndInstall", "Изтегляне и инсталиране на предварителни компоненти...\r\nМоля, изчакайте..." },
			{ "Downloading", "Изтегляне" },
			{ "Extracting", "Извличане" },
			{ "InstallDX", "Инсталиране на DirectX..." },
			{ "Installing", "Инсталиране" },
			{ "WaitingSelect", "Изчакване на избор..." }
		};

		// Token: 0x0400004B RID: 75
		private static Dictionary<string, string> frenchStrings = new Dictionary<string, string>
		{
			{ "Cancel", "Annuler" },
			{ "WindowsTitle", "Installation de Turborama " },
			{ "Next >", "Suivant >" },
			{ "< Back", "< Retour" },
			{ "Browse...", "Parcourir..." },
			{ "Install", "Installer" },
			{ "Welcome", "Bienvenue dans l'assistant d'installation de Turborama" },
			{ "WelcomeText", "Cet assistant va vous guider dans l'installation de Turborama {0} {1} sur votre ordinateur.\n\nIl est recommandé de fermer toutes les applications actives avant de continuer.\n\nCliquez sur Suivant pour continuer ou sur Annuler pour abandonner l'installation." },
			{ "CancelSure", "Êtes-vous sûr de vouloir quitter l'installation ?" },
			{ "CancelButtonTitle", "Annulation" },
			{ "AgreeText", "J'accepte les conditions d'utilisation" },
			{
				"LicenseText",
				Texts.licence
			},
			{ "LicenseIntro", "Accord de licence" },
			{ "SelectFolder", "Répertoire d'installation:" },
			{ "InstallTitle", "Dossier de destination" },
			{ "InstallInfo", "L'assistant va installer Turborama dans le dossier suivant.\nPour continuer, cliquez sur Suivant. Si vous souhaitez choisir un dossier différent, cliquez sur Parcourir." },
			{ "InstallFolderHint", "Le programme requiert au moins 3,38 Go d'espace disque disponible.\n\nN'utilisez pas de dossier avec des espaces ou des caractères spéciaux !" },
			{ "InstallCompleteDescription", "Turborama a été installé sur votre ordinateur.\n\nAppuyez sur terminer pour fermer cet assistant." },
			{ "FolderNotEmpty", "Le dossier sélectionné n'est pas vide. Veuillez choisir un dossier vide ou créez un nouveau dossier." },
			{ "ValidFolder", "Veuillez sélectionner un dossier valide." },
			{ "FailedFolder", "Erreur lors de la création du dossier: " },
			{ "ExtractFail", "Erreur lors de l'extraction des fichiers: " },
			{ "InstallFail", "Erreur lors de l'installation: " },
			{ "LaunchFail", "Erreur lors du lancement de Turborama: " },
			{ "ExeNotFound", "Exécutable non trouvé: " },
			{ "InstallComplete", "Installation terminée!" },
			{ "RunRetroBat", "Lancer TurboRama.exe" },
			{ "Finish", "Terminer" },
			{ "Error", "Erreur" },
			{ "StartupError", "Erreur au lancement de l'application, consultez le log." },
			{ "PrerequisiteIntro", "Sélectionnez les prérequis à installer." },
			{ "vcText", "Microsoft Visual C++ Runtimes Complete (2005-2022, x86 + x64) - selected" },
			{ "dx9text", "DirectX Complete: Legacy June 2010 + DirectX 11/12 via Windows Update - selected" },
			{ "dokanyText", "Dokan (permet de monter les images Xbox avec CXBX)" },
			{ "DownloadAndInstall", "Téléchargement et installation des prérequis...\r\nVeuillez patienter..." },
			{ "Downloading", "Téléchargement de" },
			{ "Extracting", "Extraction de" },
			{ "InstallDX", "Installation de DirectX..." },
			{ "Installing", "Installation de" },
			{ "WaitingSelect", "En attente des choix..." }
		};

		// Token: 0x0400004C RID: 76
		private static Dictionary<string, string> germanStrings = new Dictionary<string, string>
		{
			{ "Cancel", "Abbrechen" },
			{ "WindowsTitle", "Turborama-Installer " },
			{ "Next >", "Weiter >" },
			{ "< Back", "< Zurück" },
			{ "Browse...", "Durchsuchen..." },
			{ "Install", "Installieren" },
			{ "Welcome", "Willkommen beim Turborama-Installationsprogramm" },
			{ "WelcomeText", "Dieser Assistent führt dich durch die Installation von Turborama {0} {1}.\n\nSchließe alle laufenden Programme, bevor du fortfährst.\n\nKlicke auf Weiter, um fortzufahren, oder auf Abbrechen, um den Installer zu beenden." },
			{ "CancelSure", "Möchtest du die Installation wirklich abbrechen?" },
			{ "CancelButtonTitle", "Abbrechen" },
			{ "AgreeText", "Ich akzeptiere die Lizenzbedingungen" },
			{
				"LicenseText",
				Texts.licence
			},
			{ "LicenseIntro", "Lizenzvereinbarung" },
			{ "SelectFolder", "Installationsordner auswählen:" },
			{ "InstallTitle", "Zielordner" },
			{ "InstallInfo", "Turborama wird im unten angegebenen Ordner installiert.\n\nZum Fortfahren auf Weiter klicken, für einen anderen Ordner auf Durchsuchen." },
			{ "InstallFolderHint", "Das Programm benötigt mindestens 3,38 GB freien Speicherplatz.\n\nVermeiden Sie Ordner mit Leerzeichen oder Sonderzeichen." },
			{ "FolderNotEmpty", "Der gewählte Ordner ist nicht leer. Bitte wähle einen leeren oder neuen Ordner." },
			{ "ValidFolder", "Bitte wähle einen gültigen Installationsordner." },
			{ "FailedFolder", "Erstellen des Installationsordners fehlgeschlagen: " },
			{ "ExtractFail", "Entpacken der Installationsdaten fehlgeschlagen: " },
			{ "InstallFail", "Installation fehlgeschlagen: " },
			{ "LaunchFail", "Start der Anwendung fehlgeschlagen: " },
			{ "ExeNotFound", "Ausführbare Datei nicht gefunden: " },
			{ "InstallComplete", "Installation abgeschlossen." },
			{ "RunRetroBat", "TurboRama.exe starten" },
			{ "Finish", "Fertigstellen" },
			{ "Error", "Fehler" },
			{ "StartupError", "Startfehler aufgetreten. Siehe Logdatei für Details." },
			{ "PrerequisiteIntro", "Wähle die zu installierenden Komponenten aus, bevor du fortfährst." },
			{ "vcText", "Microsoft Visual C++ Runtimes Complete (2005-2022, x86 + x64) - selected" },
			{ "dx9text", "DirectX 9 (Altversion)" },
			{ "dokanyText", "Dokan (zum Einbinden von XBOX-Images mit CXBX)" },
			{ "DownloadAndInstall", "Komponenten werden heruntergeladen und installiert…\r\nBitte warten…" },
			{ "Downloading", "Wird heruntergeladen" },
			{ "Extracting", "Wird entpackt" },
			{ "InstallDX", "DirectX wird installiert…" },
			{ "Installing", "Wird installiert" },
			{ "WaitingSelect", "Warte auf Auswahl…" }
		};

		// Token: 0x0400004D RID: 77
		private static Dictionary<string, string> italianStrings = new Dictionary<string, string>
		{
			{ "Cancel", "Annulla" },
			{ "WindowsTitle", "Installatore Turborama " },
			{ "Next >", "Avanti >" },
			{ "< Back", "< Indietro" },
			{ "Browse...", "Sfoglia..." },
			{ "Install", "Installa" },
			{ "Welcome", "Benvenuto nel programma di installazione di Turborama" },
			{ "WelcomeText", "Questa procedura guidata ti accompagnerà nell'installazione di Turborama {0} {1} sul tuo computer.\n\nSi consiglia di chiudere tutte le applicazioni attive prima del passaggio successivo.\n\nFai clic su Avanti per continuare o su Annulla per uscire dal programma di installazione." },
			{ "CancelSure", "Sei sicuro di voler annullare l'installazione ?" },
			{ "CancelButtonTitle", "Annulla" },
			{ "AgreeText", "Accetto i termini del contratto di licenza" },
			{
				"LicenseText",
				Texts.licence
			},
			{ "LicenseIntro", "Contratto di licenza" },
			{ "SelectFolder", "Seleziona la cartella di installazione:" },
			{ "InstallTitle", "Cartella di destinazione" },
			{ "InstallInfo", "Il programma di installazione installerà Turborama nella cartella indicata di seguito.\n\nPer continuare, fai clic su Avanti. Se vuoi specificare un'altra cartella, fai clic su Sfoglia." },
			{ "InstallFolderHint", "Il programma richiede almeno 3,38 GB di spazio libero su disco.\n\nNon utilizzare cartelle con spazi o caratteri speciali." },
			{ "FolderNotEmpty", "La cartella selezionata non è vuota. Scegli una cartella vuota o una nuova cartella." },
			{ "ValidFolder", "Seleziona una cartella di installazione valida." },
			{ "FailedFolder", "Impossibile creare la cartella di installazione: " },
			{ "ExtractFail", "Impossibile estrarre i dati dell’installatore: " },
			{ "InstallFail", "Installazione non riuscita: " },
			{ "LaunchFail", "Impossibile avviare l'applicazione: " },
			{ "ExeNotFound", "Eseguibile non trovato: " },
			{ "InstallComplete", "Installazione completata!" },
			{ "RunRetroBat", "Esegui TurboRama.exe" },
			{ "Finish", "Fine" },
			{ "Error", "Errore" },
			{ "StartupError", "Si è verificato un errore di avvio. Consultare il file di registro per i dettagli." },
			{ "PrerequisiteIntro", "Seleziona i componenti da installare prima di continuare." },
			{ "vcText", "Microsoft Visual C++ Redistributable (2005–2022, x86 + x64)" },
			{ "dx9text", "DirectX 9 (Eredità)" },
			{ "dokanyText", "Dokan (usato per montare immagini XBOX con CXBX)" },
			{ "DownloadAndInstall", "Download e installazione dei prerequisiti...\r\nAttendere prego..." },
			{ "Downloading", "Download in corso" },
			{ "Extracting", "Estrazione in corso" },
			{ "InstallDX", "Installazione di DirectX..." },
			{ "Installing", "Installazione in corso" },
			{ "WaitingSelect", "In attesa della selezione..." }
		};

		// Token: 0x0400004E RID: 78
		private static Dictionary<string, string> polishStrings = new Dictionary<string, string>
		{
			{ "Cancel", "Anuluj" },
			{ "WindowsTitle", "Instalator Turborama " },
			{ "Next >", "Dalej >" },
			{ "< Back", "< Cofnij" },
			{ "Browse...", "Przeglądaj..." },
			{ "Install", "Zainstaluj" },
			{ "Welcome", "Witamy w programie instalacyjnym Turborama" },
			{ "WelcomeText", "Ten kreator poprowadzi Cię przez proces instalacji programu Turborama {0} {1} na Twoim komputerze.\n\nPrzed wykonaniem kolejnego kroku zaleca się zamknięcie wszystkich aktywnych aplikacji.\n\nKliknij Dalej, aby kontynuować, lub Anuluj, aby zamknąć instalator." },
			{ "CancelSure", "Czy na pewno chcesz anulować instalację?" },
			{ "CancelButtonTitle", "Anuluj" },
			{ "AgreeText", "Akceptuję warunki umowy licencyjnej" },
			{
				"LicenseText",
				Texts.licence
			},
			{ "LicenseIntro", "Umowa licencyjna" },
			{ "SelectFolder", "Wybierz folder instalacyjny:" },
			{ "InstallTitle", "Folder docelowy" },
			{ "InstallInfo", "Program instalacyjny zainstaluje Turborama w poniższym folderze.\n\nAby kontynuować, kliknij Dalej. Jeśli chcesz wybrać inny folder, kliknij Przeglądaj." },
			{ "InstallFolderHint", "Program wymaga co najmniej 3,38 GB wolnego miejsca na dysku.\n\nNie używaj folderów zawierających spacje lub znaki specjalne." },
			{ "FolderNotEmpty", "Wybrany folder nie jest pusty. Wybierz pusty folder lub nowy folder." },
			{ "ValidFolder", "Wybierz prawidłowy folder instalacyjny." },
			{ "FailedFolder", "Nie udało się utworzyć folderu instalacyjnego: " },
			{ "ExtractFail", "Nie udało się rozpakować danych instalatora: " },
			{ "InstallFail", "Instalacja nie powiodła się: " },
			{ "LaunchFail", "Nie udało się uruchomić aplikacji: " },
			{ "ExeNotFound", "Nie znaleziono pliku wykonywalnego: " },
			{ "InstallComplete", "Instalowanie zakończone..." },
			{ "RunRetroBat", "Uruchom TurboRama.exe" },
			{ "Finish", "Zakończ" },
			{ "Error", "Błąd" },
			{ "StartupError", "Wystąpił błąd uruchamiania. Szczegółowe informacje znajdują się w pliku dziennika." },
			{ "PrerequisiteIntro", "Przed kontynuowaniem wybierz komponenty do zainstalowania." },
			{ "vcText", "Microsoft Visual C++ Runtimes Complete (2005-2022, x86 + x64) - selected" },
			{ "dx9text", "DirectX Complete: Legacy June 2010 + DirectX 11/12 via Windows Update - selected" },
			{ "dokanyText", "Dokan (używany do montowania obrazów XBOX za pomocą CXBX)" },
			{ "DownloadAndInstall", "Pobieranie i instalowanie wymaganych komponentów...\r\nProszę czekać..." },
			{ "Downloading", "Pobieranie" },
			{ "Extracting", "Rozpakowywanie" },
			{ "InstallDX", "Instalowanie DirectX..." },
			{ "Installing", "Instalowanie" },
			{ "WaitingSelect", "Oczekiwanie na wybór..." }
		};

		// Token: 0x0400004F RID: 79
		private static Dictionary<string, string> spanishStrings = new Dictionary<string, string>
		{
			{ "Cancel", "Cancelar" },
			{ "WindowsTitle", "Instalador de Turborama " },
			{ "Next >", "Siguiente >" },
			{ "< Back", "< Atrás" },
			{ "Browse...", "Examinar..." },
			{ "Install", "Instalar" },
			{ "Welcome", "Bienvenido al programa de instalación de Turborama" },
			{ "WelcomeText", "Este asistente lo guiará a través de la instalación de Turborama {0} {1} en su computadora.\n\nSe recomienda cerrar todas las aplicaciones activas antes del siguiente paso.\n\nHaga click en Siguiente para continuar o en Cancelar para salir del instalador." },
			{ "CancelSure", "¿Está seguro de que desea cancelar la instalación?" },
			{ "CancelButtonTitle", "Cancelar" },
			{ "AgreeText", "Acepto los términos del acuerdo de licencia" },
			{
				"LicenseText",
				Texts.licence
			},
			{ "LicenseIntro", "Acuerdo de Licencia" },
			{ "SelectFolder", "Seleccione la carpeta de instalación:" },
			{ "InstallTitle", "Carpeta de destino" },
			{ "InstallInfo", "El programa de instalación instalará Turborama en la carpeta indicada a continuación.\n\nPara continuar, haga click en Siguiente. Si desea especificar otra carpeta, haga click en Examinar" },
			{ "InstallFolderHint", "El programa requiere al menos 3.38 GB de espacio libre en el disco.\n\nNo uses carpetas con espacios ni caracteres especiales." },
			{ "FolderNotEmpty", "La carpeta seleccionada no está vacía. Por favor elija una carpeta vacía o cree una nueva." },
			{ "ValidFolder", "Por favor seleccione una carpeta de instalación válida." },
			{ "FailedFolder", "Fallo al crear la carpeta de instalación: " },
			{ "ExtractFail", "Fallo al extraer los datos del instalador: " },
			{ "InstallFail", "Fallo en la instalación: " },
			{ "LaunchFail", "Fallo al iniciar la aplicacion: " },
			{ "ExeNotFound", "Ejecutable no encontrado: " },
			{ "InstallComplete", "Instalación completa" },
			{ "RunRetroBat", "Ejecutar TurboRama.exe" },
			{ "Finish", "Finalizar" },
			{ "Error", "Error" },
			{ "StartupError", "Ocurrió un error de inicio. Consulte el archivo de registro para más detalles." },
			{ "PrerequisiteIntro", "Seleccione los componentes que desea instalar antes de continuar." },
			{ "vcText", "Microsoft Visual C++ Runtimes Complete (2005-2022, x86 + x64) - selected" },
			{ "dx9text", "DirectX Complete: Legacy June 2010 + DirectX 11/12 via Windows Update - selected" },
			{ "dokanyText", "Dokan (usado para montar imágenes de XBOX con CXBX)" },
			{ "DownloadAndInstall", "Descargando e instalando los requisitos previos...\r\nPor favor espere..." },
			{ "Downloading", "Descargando" },
			{ "Extracting", "Extrayendo" },
			{ "InstallDX", "Instalando DirectX..." },
			{ "Installing", "Instalando" },
			{ "WaitingSelect", "Esperando la selección..." }
		};
	}
}
